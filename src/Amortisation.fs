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
    type ScheduleItem = {
        /// the date of amortisation
        OffsetDate: Date
        /// the offset expressed as the number of days from the start date
        OffsetDay: int<OffsetDay>
        /// any advance made on the current day, typically the principal on day 0 for a single-advance transaction
        Advances: int64<Cent> array
        /// any payment scheduled on the current day
        ScheduledPayment: int64<Cent> voption
        /// any payment scheduled on the current day
        PaymentDue: int64<Cent>
        /// any payments actually made on the current day
        ActualPayments: ActualPayment array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: int64<Cent> voption
        /// the net effect of the scheduled and actual payments, or, for future days, what the net effect would be if the scheduled payment was actually made
        NetEffect: int64<Cent>
        /// the status based on the payments and net effect
        PaymentStatus: CustomerPaymentStatus
        /// the overall balance status
        BalanceStatus: BalanceStatus
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
    with
        static member Default = {
            OffsetDate = Unchecked.defaultof<Date>
            OffsetDay = 0<OffsetDay>
            Advances = [||]
            ScheduledPayment = ValueNone
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueNone
            NetEffect = 0L<Cent>
            PaymentStatus = NoneScheduled
            BalanceStatus = OpenBalance
            NewInterest = 0L<Cent>
            NewCharges = [||]
            PrincipalPortion = 0L<Cent>
            FeesPortion = 0L<Cent>
            InterestPortion = 0L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = 0L<Cent>
            FeesBalance = 0L<Cent>
            InterestBalance = 0L<Cent>
            ChargesBalance = 0L<Cent>
        }

    [<Struct>]
    type Accumulator = {
        /// the total of scheduled payments up to and including the current day
        CumulativeScheduledPayments: int64<Cent>
        /// the total of actual payments made up to and including the current day
        CumulativeActualPayments: int64<Cent>
        /// the total of generated payments made up to and including the current day
        CumulativeGeneratedPayments: int64<Cent>
        /// the total of interest accrued up to and including the current day
        CumulativeInterest: int64<Cent>
        /// the first missed payment
        FirstMissedPayment: int64<Cent> voption
        /// the total of missed payments up to and including the current day
        CumulativeMissedPayments: int64<Cent>
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

        let getBalanceStatus principalBalance =
            if principalBalance = 0L<Cent> then
                ClosedBalance
            elif principalBalance < 0L<Cent> then
                RefundDue
            else
                OpenBalance

        appliedPayments
        |> Array.scan(fun ((si: ScheduleItem), (a: Accumulator)) ap ->
            let advances = if ap.AppliedPaymentDay = 0<OffsetDay> then [| sp.Principal |] else [||] // note: assumes single advance on day 0L<Cent>

            let earlySettlementDate = match intendedPurpose with IntendedPurpose.Quote _ -> ValueSome sp.AsOfDate | _ -> ValueNone
            let interestChargeableDays = Interest.chargeableDays sp.StartDate earlySettlementDate sp.Interest.GracePeriod sp.Interest.Holidays si.OffsetDay ap.AppliedPaymentDay

            let newInterest =
                if si.PrincipalBalance <= 0L<Cent> then // note: should this also inspect fees balance: problably not, as fees can be 0L<Cent> and also principal balance is always settled last
                    match negativeInterestOption with
                    | ApplyNegativeInterest ->
                        let dailyInterestRate = sp.Interest.RateOnNegativeBalance |> ValueOption.map Interest.Rate.daily |> ValueOption.defaultValue (Percent 0m)
                        Interest.calculate 0L<Cent> (si.PrincipalBalance + si.FeesBalance) dailyInterestRate interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
                    | DoNotApplyNegativeInterest ->
                        0L<Cent>
                else
                    let dailyInterestCap = sp.Interest.Cap.Daily |> Interest.Cap.daily (si.PrincipalBalance + si.FeesBalance) interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
                    Interest.calculate dailyInterestCap (si.PrincipalBalance + si.FeesBalance) dailyInterestRate interestChargeableDays sp.Calculation.RoundingOptions.InterestRounding
            let newInterest' = if a.CumulativeInterest + newInterest >= totalInterestCap then totalInterestCap - a.CumulativeInterest else newInterest
            let interestPortion = newInterest' + si.InterestBalance

            let confirmedPayments = ap.ActualPayments |> Array.sumBy(function ActualPayment.Confirmed ap -> ap | _ -> 0L<Cent>)
            let pendingPayments = ap.ActualPayments |> Array.sumBy(function ActualPayment.Pending ap -> ap | _ -> 0L<Cent>)

            let accumulator =
                { a with
                    CumulativeScheduledPayments = a.CumulativeScheduledPayments + (ap.ScheduledPayment |> ValueOption.defaultValue 0L<Cent>)
                    CumulativeActualPayments = a.CumulativeActualPayments + confirmedPayments + pendingPayments
                    CumulativeInterest = a.CumulativeInterest + newInterest'
                }

            let extraPaymentsBalance = a.CumulativeActualPayments - a.CumulativeScheduledPayments - a.CumulativeGeneratedPayments
            let paymentDue =
                if si.BalanceStatus = ClosedBalance then
                    0L<Cent>
                elif ap.ScheduledPayment.IsSome && extraPaymentsBalance > 0L<Cent> then
                    ap.ScheduledPayment.Value - extraPaymentsBalance
                elif ap.ScheduledPayment.IsSome then
                    Cent.min (si.PrincipalBalance + si.FeesBalance + interestPortion) ap.ScheduledPayment.Value
                else 0L<Cent>
                |> Cent.max 0L<Cent>
                |> fun p ->
                    match sp.Calculation.MinimumPayment with
                    | NoMinimumPayment -> p
                    | DeferOrWriteOff minimumPayment when p < minimumPayment -> 0L<Cent>
                    | ApplyMinimumPayment minimumPayment when p < minimumPayment -> p
                    | _ -> p

            let underpayment =
                match ap.PaymentStatus with
                | MissedPayment ->
                    paymentDue
                | Underpayment ->
                    paymentDue - ap.NetEffect
                | _ ->
                    0L<Cent>

            let newChargesTotal, incurredCharges =
                if paymentDue = 0L<Cent> then
                    0L<Cent>, [||]
                else
                    Charges.total underpayment ap.IncurredCharges, ap.IncurredCharges
            let chargesPortion = newChargesTotal + si.ChargesBalance |> Cent.max 0L<Cent>

            let generatedPayment, netEffect =
                let netEffect = if ap.AppliedPaymentDay > asOfDay then Cent.min ap.NetEffect paymentDue else ap.NetEffect
                match intendedPurpose with
                | IntendedPurpose.Quote FirstOutstanding when ap.GeneratedPayment.IsSome ->
                    match a.FirstMissedPayment with
                    | ValueSome firstMissedPayment ->
                        ValueSome firstMissedPayment
                        |> fun p -> p, (p |> ValueOption.defaultValue netEffect)
                    | ValueNone ->
                        appliedPayments
                        |> Array.tryFind(fun a -> a.AppliedPaymentDay >= ap.AppliedPaymentDay && ap.ScheduledPayment.IsSome)
                        |> toValueOption
                        |> ValueOption.bind _.ScheduledPayment
                        |> ValueOption.orElse ap.GeneratedPayment
                        |> fun p -> p, (p |> ValueOption.defaultValue netEffect)
                | IntendedPurpose.Quote AllOverdue when ap.GeneratedPayment.IsSome ->
                    ValueSome (accumulator.CumulativeMissedPayments + interestPortion + chargesPortion - confirmedPayments - pendingPayments)
                    |> fun p -> p, (p |> ValueOption.defaultValue netEffect)
                | _ ->
                    ap.GeneratedPayment, netEffect

            let accumulator =
                match ap.PaymentStatus with
                | MissedPayment | Underpayment when accumulator.FirstMissedPayment.IsNone ->
                    { accumulator with FirstMissedPayment = ValueSome paymentDue; CumulativeMissedPayments = a.CumulativeMissedPayments + paymentDue }
                | MissedPayment | Underpayment ->
                    { accumulator with CumulativeMissedPayments = a.CumulativeMissedPayments + paymentDue }
                | _ ->
                    accumulator

            let sign: int64<Cent> -> int64<Cent> = if netEffect < 0L<Cent> then (( * ) -1L) else id

            let assignable = sign netEffect - sign chargesPortion - sign interestPortion
            let feesPortion = feesPercentage |> Percent.toDecimal |> fun m -> decimal assignable * m / (1m + m) |> Cent.round RoundUp |> Cent.max 0L<Cent> |> Cent.min si.FeesBalance
            let principalPortion = Cent.max 0L<Cent> (assignable - feesPortion)
                
            let principalBalance = si.PrincipalBalance - sign principalPortion

            let carriedCharges, carriedInterest =
                if sign chargesPortion > sign netEffect then
                    chargesPortion - netEffect, interestPortion
                elif sign chargesPortion + sign interestPortion > sign netEffect then
                    0L<Cent>, interestPortion - (netEffect - chargesPortion)
                else
                    0L<Cent>, 0L<Cent>

            let offsetDate = sp.StartDate.AddDays(int ap.AppliedPaymentDay)

            let scheduleItem, generatedPayment =
                match ap.GeneratedPayment, intendedPurpose with
                | ValueNone, _
                | ValueSome _, IntendedPurpose.Statement
                | ValueSome _, IntendedPurpose.Quote FirstOutstanding
                | ValueSome _, IntendedPurpose.Quote AllOverdue ->

                    let paymentStatus =
                        match si.BalanceStatus with
                        | ClosedBalance ->
                            NoLongerRequired
                        | RefundDue ->
                            Refunded
                        | _ ->
                            if paymentDue = 0L<Cent> && confirmedPayments = 0L<Cent> && pendingPayments = 0L<Cent> && (generatedPayment.IsNone || generatedPayment.Value = 0L<Cent>) then
                                NothingDue
                            else
                                ap.PaymentStatus

                    {
                        OffsetDate = offsetDate
                        OffsetDay = ap.AppliedPaymentDay
                        Advances = advances
                        ScheduledPayment = ap.ScheduledPayment
                        PaymentDue = paymentDue
                        ActualPayments = ap.ActualPayments
                        GeneratedPayment = generatedPayment
                        NetEffect = netEffect
                        PaymentStatus = paymentStatus
                        BalanceStatus = getBalanceStatus principalBalance
                        NewInterest = newInterest'
                        NewCharges = incurredCharges
                        PrincipalPortion = sign principalPortion
                        FeesPortion = sign feesPortion
                        InterestPortion = interestPortion - carriedInterest
                        ChargesPortion = chargesPortion - carriedCharges
                        FeesRefund = 0L<Cent>
                        PrincipalBalance = principalBalance
                        FeesBalance = si.FeesBalance - sign feesPortion
                        InterestBalance = si.InterestBalance + newInterest' - interestPortion + carriedInterest
                        ChargesBalance = si.ChargesBalance + newChargesTotal - chargesPortion + carriedCharges
                    }, 0L<Cent>


                | ValueSome _, IntendedPurpose.Quote Settlement ->

                    let proRatedFees =
                        match sp.FeesAndCharges.FeesSettlement with
                        | Fees.Settlement.ProRataRefund when originalFinalPaymentDay > 0<OffsetDay> -> 
                            decimal feesTotal * decimal ap.AppliedPaymentDay / decimal originalFinalPaymentDay |> Cent.round RoundDown
                        | Fees.Settlement.DueInFull 
                        | _ -> feesTotal

                    let feesRefund =
                        match sp.FeesAndCharges.FeesSettlement with
                        | Fees.Settlement.DueInFull -> 0L<Cent>
                        | Fees.Settlement.ProRataRefund -> Cent.max 0L<Cent> (feesTotal - proRatedFees)

                    let generatedPayment = si.PrincipalBalance + si.FeesBalance - feesRefund + interestPortion + chargesPortion - confirmedPayments

                    let paymentStatus =
                        match si.BalanceStatus with
                        | ClosedBalance ->
                            NoLongerRequired
                        | _ ->
                            Generated Settlement

                    {
                        OffsetDate = offsetDate
                        OffsetDay = ap.AppliedPaymentDay
                        Advances = advances
                        ScheduledPayment = ap.ScheduledPayment
                        PaymentDue = paymentDue
                        ActualPayments = ap.ActualPayments
                        GeneratedPayment = ValueSome generatedPayment
                        NetEffect = netEffect + generatedPayment
                        PaymentStatus = paymentStatus
                        BalanceStatus = ClosedBalance
                        NewInterest = newInterest'
                        NewCharges = incurredCharges
                        PrincipalPortion = si.PrincipalBalance
                        FeesPortion = si.FeesBalance - feesRefund
                        InterestPortion = interestPortion
                        ChargesPortion = chargesPortion
                        FeesRefund = feesRefund
                        PrincipalBalance = 0L<Cent>
                        FeesBalance = 0L<Cent>
                        InterestBalance = 0L<Cent>
                        ChargesBalance = 0L<Cent>
                    }, generatedPayment

            let accumulator = { accumulator with CumulativeGeneratedPayments = a.CumulativeGeneratedPayments + generatedPayment }

            scheduleItem, accumulator

        ) (
            { ScheduleItem.Default with OffsetDate = sp.StartDate; Advances = [| sp.Principal |]; PrincipalBalance = sp.Principal; FeesBalance = feesTotal },
            { CumulativeScheduledPayments = 0L<Cent>; CumulativeActualPayments = 0L<Cent>; CumulativeGeneratedPayments = 0L<Cent>; CumulativeInterest = 0L<Cent>; FirstMissedPayment = ValueNone; CumulativeMissedPayments = 0L<Cent> }
        )
        |> fun a -> if (a |> Array.filter(fun (si, _) -> si.OffsetDay = 0<OffsetDay>) |> Array.length = 2) then a |> Array.tail else a
        |> Array.unzip
        |> fst

    /// wraps the amortisation schedule in some statistics, and optionally calculate the final APR (optional because it can be processor-intensive)
    let calculateStats sp finalAprOption items =
        let finalItem = Array.last items
        let principalTotal = items |> Array.sumBy _.PrincipalPortion
        let feesTotal = items |> Array.sumBy _.FeesPortion
        let interestTotal = items |> Array.sumBy _.InterestPortion
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
            FinalScheduledPaymentCount = items |> Array.filter(fun asi -> asi.ScheduledPayment.IsSome) |> Array.length
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
    let generate sp intendedPurpose finalAprOption negativeInterestOption actualPayments =
        voption {
            let! schedule = PaymentSchedule.calculate BelowZero sp
            let latePaymentCharge = sp.FeesAndCharges.Charges |> Array.tryPick(function Charge.LatePayment amount -> Some amount | _ -> None) |> Option.map ValueSome |> Option.defaultValue ValueNone
            let items =
                schedule.Items
                |> Array.filter _.Payment.IsSome
                |> Array.map(fun si -> { PaymentDay = si.Day; PaymentDetails = ScheduledPayment si.Payment.Value })
                |> applyPayments schedule.AsOfDay intendedPurpose sp.FeesAndCharges.LatePaymentGracePeriod latePaymentCharge actualPayments
                |> calculate sp intendedPurpose schedule.FinalPaymentDay negativeInterestOption
            return items |> calculateStats sp finalAprOption
        }
