namespace FSharp.Finance.Personal

/// calculating the principal balance over time, taking into account the effects of charges, interest and fees
module Amortisation =

    open AppliedPayment
    open CustomerPayments
    open FeesAndCharges
    open PaymentSchedule

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
        ScheduledPayment: ScheduledPaymentType
        /// any payment scheduled on the current day
        PaymentDue: int64<Cent>
        /// any payments actually made on the current day
        ActualPayments: ActualPaymentStatus array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: int64<Cent> voption
        /// the net effect of the scheduled and actual payments, or, for future days, what the net effect would be if the scheduled payment was actually made
        NetEffect: int64<Cent>
        /// the status based on the payments and net effect
        PaymentStatus: CustomerPaymentStatus
        /// the overall balance status
        BalanceStatus: BalanceStatus
        /// the new interest charged between the previous amortisation day and the current day
        NewInterest: decimal<Cent>
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
        InterestBalance: decimal<Cent>
        /// the charges balance to be carried forward
        ChargesBalance: int64<Cent>
        /// the settlement figure as of the current day
        SettlementFigure: int64<Cent>
        /// the pro-rated fees as of the current day
        ProRatedFees: int64<Cent>
    }
    with
        static member Default = {
            OffsetDate = Unchecked.defaultof<Date>
            OffsetDay = 0<OffsetDay>
            Advances = [||]
            ScheduledPayment = ScheduledPaymentType.None
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueNone
            NetEffect = 0L<Cent>
            PaymentStatus = NoneScheduled
            BalanceStatus = OpenBalance
            NewInterest = 0m<Cent>
            NewCharges = [||]
            PrincipalPortion = 0L<Cent>
            FeesPortion = 0L<Cent>
            InterestPortion = 0L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = 0L<Cent>
            FeesBalance = 0L<Cent>
            InterestBalance = 0m<Cent>
            ChargesBalance = 0L<Cent>
            SettlementFigure = 0L<Cent>
            ProRatedFees = 0L<Cent>
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
        CumulativeInterest: decimal<Cent>
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
        /// the offset day of the final cheduled payment
        FinalScheduledPaymentDay: int<OffsetDay>
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
    let calculate sp intendedPurpose (appliedPayments: AppliedPayment array) =
        let asOfDay = (sp.AsOfDate - sp.StartDate).Days * 1<OffsetDay>

        let dailyInterestRate = sp.Interest.Rate |> Interest.Rate.daily
        let totalInterestCap = sp.Interest.Cap.Total |> Interest.Cap.total sp.Principal

        let feesTotal = Fees.total sp.Principal sp.FeesAndCharges.Fees |> Cent.fromDecimalCent (ValueSome sp.Calculation.RoundingOptions.FeesRounding)

        let feesPercentage =
            if sp.Principal = 0L<Cent> then Percent 0m
            else decimal feesTotal / decimal sp.Principal |> Percent.fromDecimal

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
            let interestChargeableDays = Interest.chargeableDays sp.StartDate earlySettlementDate sp.Interest.InitialGracePeriod sp.Interest.Holidays si.OffsetDay ap.AppliedPaymentDay

            let newInterest =
                if si.PrincipalBalance <= 0L<Cent> then
                    match sp.Calculation.NegativeInterestOption with
                    | ApplyNegativeInterest ->
                        let dailyInterestRate = sp.Interest.RateOnNegativeBalance |> ValueOption.map Interest.Rate.daily |> ValueOption.defaultValue (Percent 0m)
                        Interest.calculate 0m<Cent> (si.PrincipalBalance + si.FeesBalance) dailyInterestRate interestChargeableDays
                    | DoNotApplyNegativeInterest ->
                        0m<Cent>
                else
                    let dailyInterestCap = sp.Interest.Cap.Daily |> Interest.Cap.daily (si.PrincipalBalance + si.FeesBalance) interestChargeableDays
                    Interest.calculate dailyInterestCap (si.PrincipalBalance + si.FeesBalance) dailyInterestRate interestChargeableDays

            let cappedNewInterest = if a.CumulativeInterest + newInterest >= totalInterestCap then totalInterestCap - a.CumulativeInterest else newInterest

            let confirmedPayments = ap.ActualPayments |> Array.sumBy(function ActualPaymentStatus.Confirmed ap -> ap | ActualPaymentStatus.WriteOff ap -> ap | _ -> 0L<Cent>)

            let pendingPayments = ap.ActualPayments |> Array.sumBy(function ActualPaymentStatus.Pending ap -> ap | _ -> 0L<Cent>)

            let scheduledPaymentAmount = ap.ScheduledPayment.Value

            let accumulator =
                { a with
                    CumulativeScheduledPayments = a.CumulativeScheduledPayments + scheduledPaymentAmount
                    CumulativeActualPayments = a.CumulativeActualPayments + confirmedPayments + pendingPayments
                    CumulativeInterest = a.CumulativeInterest + cappedNewInterest
                }

            let extraPaymentsBalance = a.CumulativeActualPayments - a.CumulativeScheduledPayments - a.CumulativeGeneratedPayments

            let interestPortion =
                if confirmedPayments < 0L<Cent> && si.SettlementFigure >= 0L<Cent> then
                    0L<Cent>
                else
                    cappedNewInterest + si.InterestBalance
                    |> decimal
                    |> Cent.round (ValueSome sp.Calculation.RoundingOptions.InterestRounding)

            let paymentDue =
                if si.BalanceStatus = ClosedBalance || si.BalanceStatus = RefundDue then
                    0L<Cent>
                else
                    match ap.ScheduledPayment with
                    | ScheduledPaymentType.Original amount when extraPaymentsBalance > 0L<Cent> ->
                        amount - extraPaymentsBalance
                    | ScheduledPaymentType.Original amount
                    | ScheduledPaymentType.Rescheduled amount ->
                        Cent.min (si.PrincipalBalance + si.FeesBalance + interestPortion) amount
                    | ScheduledPaymentType.None ->
                        0L<Cent>
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
                    if Charges.areApplicable sp.StartDate sp.FeesAndCharges.ChargesHolidays si.OffsetDay then
                        Charges.total underpayment ap.IncurredCharges |> Cent.fromDecimalCent (ValueSome sp.Calculation.RoundingOptions.ChargesRounding), ap.IncurredCharges
                    else
                        0L<Cent>, [||]

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
                        |> ValueOption.map _.ScheduledPayment.Value
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

            let assignable =
                if netEffect = 0L<Cent> then
                    0L<Cent>
                else
                    sign netEffect - sign chargesPortion - sign interestPortion

            let feesPortion =
                feesPercentage
                |> Percent.toDecimal
                |> fun m ->
                    if (1m + m) = 0m then 0L<Cent>
                    else decimal assignable * m / (1m + m) |> Cent.round (ValueSome RoundUp) |> Cent.max 0L<Cent> |> Cent.min si.FeesBalance

            let calculateFees originalFinalPaymentDay =
                if originalFinalPaymentDay <= 0<OffsetDay> then
                    0L<Cent>
                elif ap.AppliedPaymentDay > originalFinalPaymentDay then
                    0L<Cent>
                else
                    decimal feesTotal * (decimal originalFinalPaymentDay - decimal ap.AppliedPaymentDay) / decimal originalFinalPaymentDay |> Cent.round (ValueSome RoundUp)

            let proRatedFees =
                match sp.FeesAndCharges.FeesSettlement with
                | Fees.Settlement.ProRataRefund ->
                    match sp.ScheduleType with
                    | ScheduleType.Original ->
                        let originalFinalPaymentDay = sp.PaymentSchedule |> paymentDays sp.StartDate |> Array.vTryLastBut 0 |> ValueOption.defaultValue 0<OffsetDay>
                        calculateFees originalFinalPaymentDay
                    | ScheduleType.Rescheduled originalFinalPaymentDay ->
                        calculateFees originalFinalPaymentDay
                | _ ->
                    si.FeesBalance - feesPortion

            let feesRefund =
                match sp.FeesAndCharges.FeesSettlement with
                | Fees.Settlement.DueInFull ->
                    0L<Cent>
                | Fees.Settlement.ProRataRefund ->
                    Cent.max 0L<Cent> proRatedFees

            let generatedSettlementPayment = si.PrincipalBalance + si.FeesBalance - feesRefund + interestPortion + chargesPortion

            let feesPortion', feesRefund' =
                if feesPortion > 0L<Cent> && generatedSettlementPayment <= netEffect then
                    Cent.max 0L<Cent> (si.FeesBalance - feesRefund), feesRefund
                else
                    sign feesPortion, 0L<Cent>

            let principalPortion = Cent.max 0L<Cent> (assignable - feesPortion')

            let principalBalance = si.PrincipalBalance - sign principalPortion

            let paymentDue', netEffect', principalPortion', principalBalance' =
                if ap.PaymentStatus = NotYetDue && feesRefund' > 0L<Cent> && principalBalance < 0L<Cent> then
                    paymentDue + principalBalance, netEffect + principalBalance, sign principalPortion + principalBalance, 0L<Cent>
                else
                    paymentDue, netEffect, sign principalPortion, principalBalance

            let carriedCharges, carriedInterest =
                if sign chargesPortion > sign netEffect then
                    chargesPortion - netEffect, Cent.toDecimalCent interestPortion
                elif netEffect = 0L<Cent> && interestPortion < 0L<Cent> then
                    0L<Cent>, Cent.toDecimalCent interestPortion
                elif sign chargesPortion + sign interestPortion > sign netEffect then
                    0L<Cent>, Cent.toDecimalCent (interestPortion - (netEffect - chargesPortion))
                else
                    0L<Cent>, 0m<Cent>

            let carriedInterest' = carriedInterest |> decimal |> Cent.round (ValueSome sp.Calculation.RoundingOptions.InterestRounding)

            let offsetDate = sp.StartDate.AddDays(int ap.AppliedPaymentDay)

            let balanceStatus = getBalanceStatus principalBalance'

            let settlementItem () =
                let paymentStatus =
                    match si.BalanceStatus with
                    | ClosedBalance ->
                        NoLongerRequired
                    | _ ->
                        Generated Settlement

                let generatedSettlementPayment' = generatedSettlementPayment - netEffect'

                {
                    OffsetDate = offsetDate
                    OffsetDay = ap.AppliedPaymentDay
                    Advances = advances
                    ScheduledPayment = ap.ScheduledPayment
                    PaymentDue = paymentDue
                    ActualPayments = ap.ActualPayments
                    GeneratedPayment = ValueSome generatedSettlementPayment'
                    NetEffect = netEffect + generatedSettlementPayment'
                    PaymentStatus = paymentStatus
                    BalanceStatus = ClosedBalance
                    NewInterest = cappedNewInterest
                    NewCharges = incurredCharges
                    PrincipalPortion = si.PrincipalBalance
                    FeesPortion = si.FeesBalance - feesRefund
                    InterestPortion = interestPortion
                    ChargesPortion = chargesPortion
                    FeesRefund = feesRefund
                    PrincipalBalance = 0L<Cent>
                    FeesBalance = 0L<Cent>
                    InterestBalance = 0m<Cent>
                    ChargesBalance = 0L<Cent>
                    SettlementFigure = generatedSettlementPayment'
                    ProRatedFees = proRatedFees
                }, generatedSettlementPayment'

            let scheduleItem, generatedPayment =
                match ap.GeneratedPayment, intendedPurpose with
                | ValueSome _, IntendedPurpose.Quote Settlement ->
                    settlementItem ()
                | ValueSome _, IntendedPurpose.Quote (FutureSettlement day) when day = ap.AppliedPaymentDay ->
                    settlementItem ()

                | ValueNone, _
                | ValueSome _, IntendedPurpose.Statement
                | ValueSome _, IntendedPurpose.Quote FirstOutstanding
                | ValueSome _, IntendedPurpose.Quote AllOverdue
                | ValueSome _, IntendedPurpose.Quote (FutureSettlement _) ->

                    let paymentStatus =
                        match si.BalanceStatus with
                        | ClosedBalance ->
                            NoLongerRequired
                        | RefundDue when netEffect' < 0L<Cent> ->
                            Refunded
                        | RefundDue when netEffect' > 0L<Cent> ->
                            Overpayment
                        | RefundDue ->
                            NoLongerRequired
                        | _ ->
                            if paymentDue' = 0L<Cent> && confirmedPayments = 0L<Cent> && pendingPayments = 0L<Cent> && (generatedPayment.IsNone || generatedPayment.Value = 0L<Cent>) then
                                NothingDue
                            else
                                ap.PaymentStatus

                    let generatedSettlementPayment' =
                        if pendingPayments > 0L<Cent> then
                            0L<Cent>
                        elif ap.PaymentStatus = NotYetDue then
                            generatedSettlementPayment
                        else
                            generatedSettlementPayment - netEffect'

                    {
                        OffsetDate = offsetDate
                        OffsetDay = ap.AppliedPaymentDay
                        Advances = advances
                        ScheduledPayment = ap.ScheduledPayment
                        PaymentDue = paymentDue'
                        ActualPayments = ap.ActualPayments
                        GeneratedPayment = generatedPayment
                        NetEffect = netEffect'
                        PaymentStatus = paymentStatus
                        BalanceStatus = balanceStatus
                        NewInterest = cappedNewInterest
                        NewCharges = incurredCharges
                        PrincipalPortion = principalPortion'
                        FeesPortion = feesPortion'
                        InterestPortion = interestPortion - carriedInterest'
                        ChargesPortion = chargesPortion - carriedCharges
                        FeesRefund = feesRefund'
                        PrincipalBalance = principalBalance'
                        FeesBalance = si.FeesBalance - feesPortion' - feesRefund'
                        InterestBalance =
                            si.InterestBalance + cappedNewInterest - Cent.toDecimalCent interestPortion + carriedInterest
                            |> fun ib -> if ib < 1m<Cent> then 0m<Cent> else ib
                        ChargesBalance = si.ChargesBalance + newChargesTotal - chargesPortion + carriedCharges
                        SettlementFigure = if paymentStatus = NoLongerRequired then 0L<Cent> else generatedSettlementPayment'
                        ProRatedFees = if paymentStatus = NoLongerRequired then 0L<Cent> else proRatedFees
                    }, 0L<Cent>

            let accumulator = { accumulator with CumulativeGeneratedPayments = a.CumulativeGeneratedPayments + generatedPayment }

            scheduleItem, accumulator

        ) (
            { ScheduleItem.Default with OffsetDate = sp.StartDate; Advances = [| sp.Principal |]; PrincipalBalance = sp.Principal; FeesBalance = feesTotal; SettlementFigure = sp.Principal + feesTotal; ProRatedFees = feesTotal },
            { CumulativeScheduledPayments = 0L<Cent>; CumulativeActualPayments = 0L<Cent>; CumulativeGeneratedPayments = 0L<Cent>; CumulativeInterest = 0m<Cent>; FirstMissedPayment = ValueNone; CumulativeMissedPayments = 0L<Cent> }
        )
        |> fun a -> if (a |> Array.filter(fun (si, _) -> si.OffsetDay = 0<OffsetDay>) |> Array.length = 2) then a |> Array.tail else a
        |> Array.unzip
        |> fst

    /// wraps the amortisation schedule in some statistics, and optionally calculate the final APR (optional because it can be processor-intensive)
    let calculateStats sp intendedPurpose items =
        let finalItem = Array.last items
        let principalTotal = items |> Array.sumBy _.PrincipalPortion
        let feesTotal = items |> Array.sumBy _.FeesPortion
        let interestTotal = items |> Array.sumBy _.InterestPortion
        let chargesTotal = items |> Array.sumBy _.ChargesPortion
        let feesRefund = finalItem.FeesRefund
        let finalPaymentDay = finalItem.OffsetDay
        let finalBalanceStatus = finalItem.BalanceStatus
        let finalAprSolution =
            match intendedPurpose with
            | IntendedPurpose.Quote Settlement | IntendedPurpose.Quote (FutureSettlement _) when finalBalanceStatus = ClosedBalance ->
                items
                |> Array.filter(fun asi -> asi.NetEffect > 0L<Cent>)
                |> Array.map(fun asi -> { Apr.TransferType = Apr.Payment; Apr.TransferDate = sp.StartDate.AddDays(int asi.OffsetDay); Apr.Amount = asi.NetEffect })
                |> Apr.calculate sp.Calculation.AprMethod sp.Principal sp.StartDate
                |> ValueSome
            | _ ->
                 ValueNone
        let scheduledPaymentItems = items |> Array.filter(fun si -> si.ScheduledPayment.IsSome)
        {
            ScheduleItems = items
            FinalScheduledPaymentDay = scheduledPaymentItems |> Array.last |> _.OffsetDay
            FinalScheduledPaymentCount = scheduledPaymentItems |> Array.length
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
    let generate sp intendedPurpose scheduledPaymentType actualPayments =
        let payments =
            match sp.PaymentSchedule with
            | RegularSchedule _ ->
                let schedule = PaymentSchedule.calculate BelowZero sp
                match schedule with
                | ValueSome s ->
                    s.Items
                    |> Array.filter _.Payment.IsSome
                    |> Array.map(fun si -> {
                        PaymentDay = si.Day
                        PaymentDetails =
                            match sp.ScheduleType with
                            | ScheduleType.Original -> ScheduledPayment (ScheduledPaymentType.Original si.Payment.Value)
                            | ScheduleType.Rescheduled _ -> ScheduledPayment (ScheduledPaymentType.Rescheduled si.Payment.Value)
                    })
                | ValueNone ->
                    [||]
            | RegularFixedSchedule (unitPeriodConfig, paymentCount, paymentAmount) ->
                UnitPeriod.generatePaymentSchedule paymentCount UnitPeriod.Direction.Forward unitPeriodConfig
                |> Array.map(fun d ->
                    {
                        PaymentDay = OffsetDay.fromDate sp.AsOfDate d
                        PaymentDetails =
                            match sp.ScheduleType with
                            | ScheduleType.Original -> ScheduledPayment (ScheduledPaymentType.Original paymentAmount)
                            | ScheduleType.Rescheduled _ -> ScheduledPayment (ScheduledPaymentType.Rescheduled paymentAmount)
                    }
                )
            | IrregularSchedule payments ->
                payments

        if Array.isEmpty payments then ValueNone else

        let asOfDay = sp.AsOfDate |> OffsetDay.fromDate sp.StartDate
        let latePaymentCharge = sp.FeesAndCharges.Charges |> Array.tryPick(function Charge.LatePayment _ as c -> Some c | _ -> None) |> toValueOption

        payments
        |> applyPayments asOfDay intendedPurpose sp.FeesAndCharges.LatePaymentGracePeriod latePaymentCharge sp.FeesAndCharges.ChargesGrouping sp.Calculation.PaymentTimeout actualPayments
        |> calculate sp intendedPurpose
        |> Array.trimEnd(fun si -> match sp.ScheduleType with ScheduleType.Rescheduled _ when si.PaymentStatus = NoLongerRequired -> true | _ -> false) // remove extra items from rescheduling
        |> calculateStats sp intendedPurpose
        |> ValueSome
