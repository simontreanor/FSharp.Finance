namespace FSharp.Finance.Personal

/// calculating the principal balance over time, taking into account the effects of charges, interest and fees
module Amortisation =

    open CustomerPayments
    open PaymentSchedule
    open AppliedPayment

    /// the status of the balance on a given offset day
    [<Struct>]
    type BalanceStatus =
        /// the balance has been settled in full
        | ClosedBalance
        /// the balance is open, meaning further payments will be required to settle it
        | OpenBalance
        /// due to an overpayment or a refund of charges, a refund is due
        | RefundDue

    /// amortisation schedule item showing apportionment of payments to principal, fees, interest and charges
    [<Struct>]
    type ScheduleItem = {
        /// the date of amortisation
        OffsetDate: Date
        /// the offset expressed as the number of days from the start date
        OffsetDay: int<OffsetDay>
        /// any advance made on the current day, typically the principal on day 0 for a single-advance transaction
        Advances: int64<Cent> array
        /// any payment scheduled on the current day
        ScheduledPayment: int64<Cent>
        /// any payments actually made on the current day
        ActualPayments: int64<Cent> array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: int64<Cent>
        /// the net effect of the scheduled and actual payments, or, for future days, what the net effect would be if the scheduled payment was actually made
        NetEffect: int64<Cent>
        /// the status based on the payments and net effect
        PaymentStatus: CustomerPaymentStatus voption
        /// the overall balance status
        BalanceStatus: BalanceStatus
        /// the total of interest accrued up to and including the current day
        CumulativeInterest: int64<Cent>
        /// the new interest charged between the previous amortisation day and the current day
        NewInterest: int64<Cent>
        /// any new charges incurred between the previous amortisation day and the current day
        NewCharges: Charge array
        /// the portion of the net effect assigned to the principal
        PrincipalPortion: int64<Cent>
        /// the portion of the net effect assigned to the fees
        FeesPortion: int64<Cent>
        /// the portion of the net effect assigned to the interest
        InterestPortion: int64<Cent>
        /// the portion of the net effect assigned to the charges
        ChargesPortion: int64<Cent>
        /// any fee refund, on the final amortisation day, if the fees are pro-rated in the event of early settlement
        FeesRefund: int64<Cent>
        /// the principal balance to be carried forward
        PrincipalBalance: int64<Cent>
        /// the fees balance to be carried forward
        FeesBalance: int64<Cent>
        /// the interest balance to be carried forward
        InterestBalance: int64<Cent>
        /// the charges balance to be carried forward
        ChargesBalance: int64<Cent>
    }

    /// a schedule showing the amortisation, itemising the effects of payments and calculating balances for each item, and producing some final statistics resulting from the calculations
    [<Struct>]
    type Schedule = {
        /// a list of amortisation items, showing the events and calculations for a particular offset day
        ScheduleItems: ScheduleItem array
        /// the final number of scheduled payments in the schedule
        FinalScheduledPaymentCount: int
        /// the final number of actual payments in the schedule (multiple payments made on the same day are counted separately)
        FinalActualPaymentCount: int
        /// the APR based on the actual payments made and their timings
        FinalApr: (Solution * Percent voption) voption
        /// the final ratio of (fees + interest + charges) to principal
        FinalCostToBorrowingRatio: Percent
        /// the daily interest rate derived from interest over (principal + fees), ignoring charges 
        EffectiveInterestRate: Interest.Rate
    }

    /// calculate amortisation schedule detailing how elements (principal, fees, interest and charges) are paid off over time
    let calculate sp intendedPurpose originalFinalPaymentDay negativeInterestOption (appliedPayments: AppliedPayment array) =
        let asOfDay = (sp.AsOfDate - sp.StartDate).Days * 1<OffsetDay>
        let dailyInterestRate = sp.Interest.Rate |> Interest.Rate.daily
        let totalInterestCap = sp.Interest.Cap.Total |> Interest.Cap.total sp.Principal sp.Calculation.RoundingOptions.InterestRounding
        let feesTotal = Fees.total sp.Principal sp.FeesAndCharges.Fees
        let feesPercentage = decimal feesTotal / decimal sp.Principal |> Percent.fromDecimal
        let appliedPayments' =
            if Array.isEmpty appliedPayments || (Array.head appliedPayments |> fun ap -> ap.AppliedPaymentDay <> 0<OffsetDay>) then // deals both with no payments and manual payment schedules
                let dummyPayment: AppliedPayment = { AppliedPaymentDay = 0<OffsetDay>; ScheduledPayment = 0L<Cent>; ActualPayments = [||]; GeneratedPayment = 0L<Cent>; IncurredCharges = [||]; NetEffect = 0L<Cent>; PaymentStatus = ValueNone }
                Array.concat [| [| dummyPayment |]; appliedPayments |]
            else
                appliedPayments
        let dayZeroPayment = appliedPayments' |> Array.head
        let ``don't apportion for a refund`` paymentTotal amount = if paymentTotal < 0L<Cent> then 0L<Cent> else amount
        let dayZeroPrincipalPortion = decimal dayZeroPayment.NetEffect / (1m + Percent.toDecimal feesPercentage) |> Cent.round RoundDown
        let dayZeroPrincipalBalance = sp.Principal - dayZeroPrincipalPortion
        let dayZeroFeesPortion = dayZeroPayment.NetEffect - dayZeroPrincipalPortion
        let dayZeroFeesBalance = feesTotal - dayZeroFeesPortion
        let getBalanceStatus principalBalance = if principalBalance = 0L<Cent> then ClosedBalance elif principalBalance < 0L<Cent> then RefundDue else OpenBalance
        let advance = {
            OffsetDate = sp.StartDate
            OffsetDay = 0<OffsetDay>
            Advances = [| sp.Principal |]
            ScheduledPayment = dayZeroPayment.ScheduledPayment
            ActualPayments = dayZeroPayment.ActualPayments
            GeneratedPayment = dayZeroPayment.GeneratedPayment
            NetEffect = dayZeroPayment.NetEffect
            PaymentStatus = dayZeroPayment.PaymentStatus
            BalanceStatus = getBalanceStatus dayZeroPrincipalBalance
            CumulativeInterest = 0L<Cent>
            NewInterest = 0L<Cent>
            NewCharges = [||]
            PrincipalPortion = dayZeroPrincipalPortion
            FeesPortion = dayZeroFeesPortion
            InterestPortion = 0L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = dayZeroPrincipalBalance
            FeesBalance = dayZeroFeesBalance
            InterestBalance = 0L<Cent>
            ChargesBalance = 0L<Cent>
        }
        appliedPayments'
        |> Array.tail
        |> Array.scan(fun (a: ScheduleItem) ap ->
            let underpayment =
                match ap.PaymentStatus with
                | ValueSome MissedPayment -> ap.ScheduledPayment
                | ValueSome Underpayment -> ap.ScheduledPayment - ap.NetEffect
                | _ -> 0L<Cent>

            let newChargesTotal = Charges.total underpayment ap.IncurredCharges
            let chargesPortion = newChargesTotal + a.ChargesBalance |> ``don't apportion for a refund`` ap.NetEffect

            let earlySettlementDate = match intendedPurpose with IntendedPurpose.Quote (ValueSome esd) -> ValueSome esd | _ -> ValueNone
            let interestChargeableDays = Interest.chargeableDays sp.StartDate earlySettlementDate sp.Interest.GracePeriod sp.Interest.Holidays a.OffsetDay ap.AppliedPaymentDay

            let newInterest =
                if a.PrincipalBalance <= 0L<Cent> then // note: should this also inspect fees balance: problably not, as fees can be zero and also principal balance is always settled last
                    match negativeInterestOption with
                    | ApplyNegativeInterest ->
                        let dailyInterestRate = sp.Interest.RateOnNegativeBalance |> ValueOption.map Interest.Rate.daily |> ValueOption.defaultValue (Percent 0m)
                        Interest.calculate 0L<Cent> (a.PrincipalBalance + a.FeesBalance) dailyInterestRate interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
                    | DoNotApplyNegativeInterest ->
                        0L<Cent>
                else
                    let dailyInterestCap = sp.Interest.Cap.Daily |> Interest.Cap.daily (a.PrincipalBalance + a.FeesBalance) interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
                    Interest.calculate dailyInterestCap (a.PrincipalBalance + a.FeesBalance) dailyInterestRate interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
            let newInterest' = a.CumulativeInterest + newInterest |> fun i -> if i >= totalInterestCap then totalInterestCap - a.CumulativeInterest else newInterest
            let interestPortion = newInterest' + a.InterestBalance //|> ``don't apportion for a refund`` ap.NetEffect
            
            let feesRemaining =
                match sp.FeesAndCharges.FeesSettlement with
                | Fees.Settlement.ProRataRefund ->
                    if ap.AppliedPaymentDay = 0<OffsetDay> then
                        feesTotal
                    elif ap.AppliedPaymentDay > originalFinalPaymentDay then
                        0L<Cent>
                    else
                        let feesDue = decimal feesTotal * decimal ap.AppliedPaymentDay / decimal originalFinalPaymentDay |> Cent.round RoundDown
                        Cent.max 0L<Cent> (feesTotal - feesDue)
                | Fees.Settlement.DueInFull ->
                    feesTotal

            let settlementFigure = a.PrincipalBalance + a.FeesBalance - feesRemaining + interestPortion + chargesPortion
            let isSettlement = ap.NetEffect = settlementFigure
            let isExtraPayment = ap.ScheduledPayment = 0L<Cent> && ap.NetEffect > settlementFigure && ap.AppliedPaymentDay <= asOfDay
            let isExcessPayment = ap.NetEffect > settlementFigure && ap.AppliedPaymentDay <= asOfDay
            let isProjection = ap.NetEffect > settlementFigure && ap.AppliedPaymentDay > asOfDay

            let feesRefund =
                match sp.FeesAndCharges.FeesSettlement with
                | Fees.Settlement.DueInFull -> 0L<Cent>
                | Fees.Settlement.ProRataRefund -> if isProjection || isExtraPayment || isExcessPayment || isSettlement then feesRemaining else 0L<Cent>

            let actualPayment = abs ap.NetEffect
            let sign: int64<Cent> -> int64<Cent> = if ap.NetEffect < 0L<Cent> then (( * ) -1L) else id

            let principalPortion, feesPortion =
                if (chargesPortion > actualPayment) || (chargesPortion + interestPortion > actualPayment) then
                    0L<Cent>, 0L<Cent>
                else
                    if isProjection || isExtraPayment || isExcessPayment || isSettlement then
                        let feesPayment = a.FeesBalance - feesRemaining
                        actualPayment - chargesPortion - sign interestPortion - feesPayment, feesPayment
                    else
                        let principalPayment = decimal (actualPayment - chargesPortion - sign interestPortion) / (1m + Percent.toDecimal feesPercentage) |> Cent.round RoundDown
                        principalPayment, actualPayment - chargesPortion - sign interestPortion - principalPayment

            let principalBalance = a.PrincipalBalance - sign principalPortion

            let carriedCharges, carriedInterest =
                if chargesPortion > actualPayment then
                    chargesPortion - actualPayment, interestPortion
                elif chargesPortion + interestPortion > actualPayment then
                    0L<Cent>, interestPortion - (actualPayment - chargesPortion)
                else
                    0L<Cent>, 0L<Cent>

            let finalPaymentAdjustment = if isProjection && (ap.ScheduledPayment > abs principalBalance) then principalBalance else 0L<Cent>

            {
                OffsetDate = sp.StartDate.AddDays(int ap.AppliedPaymentDay)
                OffsetDay = ap.AppliedPaymentDay
                Advances = [||]
                ScheduledPayment = ap.ScheduledPayment + finalPaymentAdjustment
                ActualPayments = ap.ActualPayments
                GeneratedPayment = ap.GeneratedPayment
                NetEffect = ap.NetEffect + finalPaymentAdjustment
                PaymentStatus = if a.BalanceStatus = ClosedBalance then ValueNone (*elif isExtraPayment then ValueSome ExtraPayment elif isExcessPayment then ValueSome ExcessPayment*) else ap.PaymentStatus
                BalanceStatus = getBalanceStatus (principalBalance - finalPaymentAdjustment)
                CumulativeInterest = a.CumulativeInterest + newInterest'
                NewInterest = newInterest'
                NewCharges = ap.IncurredCharges
                PrincipalPortion = sign (principalPortion + finalPaymentAdjustment)
                FeesPortion = sign feesPortion
                InterestPortion = interestPortion - carriedInterest
                ChargesPortion = chargesPortion - carriedCharges
                FeesRefund = feesRefund
                PrincipalBalance = principalBalance - finalPaymentAdjustment
                FeesBalance = a.FeesBalance - sign feesPortion - feesRefund
                InterestBalance = a.InterestBalance + newInterest' - interestPortion + carriedInterest
                ChargesBalance = a.ChargesBalance + newChargesTotal - chargesPortion + carriedCharges
            }
        ) advance
        |> Array.takeWhile(fun a -> a.OffsetDay = 0<OffsetDay> || (a.OffsetDay > 0<OffsetDay> && a.PaymentStatus.IsSome))

    /// wraps the amortisation schedule in some statistics, and optionally calculate the final APR (optional because it can be processor-intensive)
    let calculateStats sp finalAprOption items =
        let finalItem = Array.last items
        let principalTotal = items |> Array.sumBy _.PrincipalPortion
        let feesTotal = items |> Array.sumBy _.FeesPortion
        let interestTotal = finalItem.CumulativeInterest
        let chargesTotal = items |> Array.sumBy _.ChargesPortion
        let feesRefund = finalItem.FeesRefund
        let finalPaymentDay = finalItem.OffsetDay
        let finalBalanceStatus = finalItem.BalanceStatus
        let finalAprSolution =
            match finalAprOption with
            | CalculateFinalApr when finalBalanceStatus = ClosedBalance ->
                items
                |> Array.filter(fun asi -> asi.NetEffect > 0L<Cent>)
                |> Array.map(fun asi -> { Apr.TransferType = Apr.Payment; Apr.TransferDate = sp.StartDate.AddDays(int asi.OffsetDay); Apr.Amount = asi.NetEffect })
                |> Apr.calculate sp.Calculation.AprMethod sp.Principal sp.StartDate
                |> ValueSome
            | CalculateFinalApr
            | DoNotCalculateFinalApr ->
                 ValueNone
        {
            ScheduleItems = items
            FinalScheduledPaymentCount = items |> Array.filter(fun asi -> asi.ScheduledPayment > 0L<Cent>) |> Array.length
            FinalActualPaymentCount = items |> Array.sumBy(fun asi -> Array.length asi.ActualPayments)
            FinalApr = finalAprSolution |> ValueOption.map(fun s -> s, Apr.toPercent sp.Calculation.AprMethod s)
            FinalCostToBorrowingRatio =
                if principalTotal = 0L<Cent> then Percent 0m
                else decimal (feesTotal + interestTotal + chargesTotal) / decimal principalTotal |> Percent.fromDecimal |> Percent.round 2
            EffectiveInterestRate =
                if finalPaymentDay = 0<OffsetDay> || principalTotal + feesTotal - feesRefund = 0L<Cent> then 0m
                else (decimal interestTotal / decimal (principalTotal + feesTotal - feesRefund)) / decimal finalPaymentDay
                |> Percent |> Interest.Daily
        }


    /// generates an amortisation schedule and final statistics
    let generate sp intendedPurpose finalAprOption negativeInterestOption generatedPayment actualPayments =
        voption {
            let! schedule = PaymentSchedule.calculate BelowZero sp
            let latePaymentCharge = sp.FeesAndCharges.Charges |> Array.tryPick(function Charge.LatePayment amount -> Some amount | _ -> None) |> Option.map ValueSome |> Option.defaultValue ValueNone
            let items =
                schedule
                |> _.Items
                |> Array.filter(fun si ->
                    match intendedPurpose with
                    | IntendedPurpose.Quote (ValueSome earlySettlementDate) ->
                        let earlySettlementDay = earlySettlementDate |> OffsetDay.fromDate sp.StartDate
                        si.Day <= earlySettlementDay
                    | _ -> true
                )
                |> Array.map(fun si -> { PaymentDay = si.Day; PaymentDetails = ScheduledPayment si.Payment })
                |> applyPayments schedule.AsOfDay intendedPurpose sp.FeesAndCharges.LatePaymentGracePeriod latePaymentCharge generatedPayment actualPayments
                |> calculate sp intendedPurpose schedule.FinalPaymentDay negativeInterestOption
            return items |> calculateStats sp finalAprOption
        }
