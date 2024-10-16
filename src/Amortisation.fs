namespace FSharp.Finance.Personal

/// calculating the principal balance over time, taking into account the effects of charges, interest and fees
module Amortisation =

    open AppliedPayment
    open Calculation
    open DateDay
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
        /// any advance made on the current day, typically the principal on day 0 for a single-advance transaction
        Advances: int64<Cent> array
        /// any payment scheduled on the current day
        ScheduledPayment: ScheduledPayment
        /// the window during which a scheduled payment can be made; if the date is missed, the payment is late, but if the window is missed, the payment is missed
        Window: int
        /// any payment scheduled on the current day
        PaymentDue: int64<Cent>
        /// any payments actually made on the current day
        ActualPayments: ActualPayment array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: GeneratedPayment
        /// the net effect of the scheduled and actual payments, or, for future days, what the net effect would be if the scheduled payment was actually made
        NetEffect: int64<Cent>
        /// the status based on the payments and net effect
        PaymentStatus: PaymentStatus
        /// the overall balance status
        BalanceStatus: BalanceStatus
        /// any new charges incurred between the previous amortisation day and the current day
        NewCharges: Charge.ChargeType array
        /// the portion of the net effect assigned to the charges
        ChargesPortion: int64<Cent>
        /// the simple interest initially calculated at the start date
        OriginalSimpleInterest: int64<Cent>
        /// the interest initially calculated according to the interest method at the start date
        ContractualInterest: decimal<Cent>
        /// the simple interest accruable between the previous amortisation day and the current day
        SimpleInterest: decimal<Cent>
        /// the new interest charged between the previous amortisation day and the current day, less any initial interest
        NewInterest: decimal<Cent>
        /// the portion of the net effect assigned to the interest
        InterestPortion: int64<Cent>
        /// any fee refund, on the final amortisation day, if the fees are pro-rated in the event of early settlement
        FeesRefund: int64<Cent>
        /// the portion of the net effect assigned to the fees
        FeesPortion: int64<Cent>
        /// the portion of the net effect assigned to the principal
        PrincipalPortion: int64<Cent>
        /// the charges balance to be carried forward
        ChargesBalance: int64<Cent>
        /// the interest balance to be carried forward
        InterestBalance: decimal<Cent>
        /// the fees balance to be carried forward
        FeesBalance: int64<Cent>
        /// the principal balance to be carried forward
        PrincipalBalance: int64<Cent>
        /// the settlement figure as of the current day
        SettlementFigure: int64<Cent>
        /// the pro-rated fees as of the current day
        FeesRefundIfSettled: int64<Cent>
    }
    
    /// amortisation schedule item showing apportionment of payments to principal, fees, interest and charges
    module ScheduleItem =
        let initial = {
            Window = 0
            OffsetDate = Unchecked.defaultof<Date>
            Advances = [||]
            ScheduledPayment = ScheduledPayment.zero
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = NoGeneratedPayment
            NetEffect = 0L<Cent>
            PaymentStatus = NoneScheduled
            BalanceStatus = OpenBalance
            OriginalSimpleInterest = 0L<Cent>
            ContractualInterest = 0m<Cent>
            SimpleInterest = 0m<Cent>
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
            FeesRefundIfSettled = 0L<Cent>
        }

    /// a container for aggregating figures separately from the main schedule
    [<Struct>]
    type Accumulator = {
        /// the total of scheduled payments up to the current day
        CumulativeScheduledPayments: int64<Cent>
        /// the total of actual payments made up to the current day
        CumulativeActualPayments: int64<Cent>
        /// the total of generated payments made up to the current day
        CumulativeGeneratedPayments: int64<Cent>
        /// the total of fees paid up to the current day
        CumulativeFees: int64<Cent>
        /// the total of interest accrued up to the current day
        CumulativeInterest: decimal<Cent>
        /// the total of interest portions up to the current day
        CumulativeInterestPortions: int64<Cent>
        /// the total of simple interest accrued up to the current day
        CumulativeSimpleInterestM: decimal<Cent>
    }

    /// a schedule showing the amortisation, itemising the effects of payments and calculating balances for each item, and producing some final statistics resulting from the calculations
    [<Struct>]
    type Schedule = {
        /// a list of amortisation items, showing the events and calculations for a particular offset day
        ScheduleItems: Map<int<OffsetDay>, ScheduleItem>
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
    let internal calculate sp intendedPurpose (initialInterestBalanceL: int64<Cent>) (appliedPayments: Map<int<OffsetDay>, AppliedPayment>) =
        let asOfDay = (sp.AsOfDate - sp.StartDate).Days * 1<OffsetDay>

        let initialInterestBalanceM = Cent.toDecimalCent initialInterestBalanceL

        let totalInterestCapM = sp.InterestConfig.Cap.TotalAmount |> Interest.Cap.total sp.Principal

        let feesTotal = Fee.grandTotal sp.FeeConfig sp.Principal ValueNone

        let feesPercentage =
            if sp.Principal = 0L<Cent> then
                Percent 0m
            else
                decimal feesTotal / decimal sp.Principal |> Percent.fromDecimal

        let getBalanceStatus principalBalance =
            if principalBalance = 0L<Cent> then
                ClosedBalance
            elif principalBalance < 0L<Cent> then
                RefundDue
            else
                OpenBalance


        let isSettledWithinGracePeriod =
            match intendedPurpose with
            | IntendedPurpose.SettlementOn day ->
                int day <= int sp.InterestConfig.InitialGracePeriod
            | IntendedPurpose.SettlementOnAsOfDay ->
                int <| OffsetDay.fromDate sp.StartDate sp.AsOfDate <= int sp.InterestConfig.InitialGracePeriod
            | _ ->
                false

        let dailyInterestRates fromDay toDay = Interest.dailyRates sp.StartDate isSettledWithinGracePeriod sp.InterestConfig.StandardRate sp.InterestConfig.PromotionalRates fromDay toDay

        let (|NotPaidAtAll|SomePaid|FullyPaid|) (actualPaymentTotal, paymentDueTotal) =
            if actualPaymentTotal = 0L<Cent> then
                NotPaidAtAll
            elif actualPaymentTotal < paymentDueTotal then
                SomePaid (paymentDueTotal - actualPaymentTotal)
            else
                FullyPaid

        let markMissedPaymentsAsLate (schedule: Map<int<OffsetDay>, ScheduleItem>) =
            schedule
            |> Map.toArray
            |> Array.groupBy (snd >> _.Window)
            |> Array.map snd
            |> Array.filter (Array.isEmpty >> not)
            |> Array.map(fun v ->
                {|
                    OffsetDay = v |> Array.head |> fst
                    PaymentDueTotal = v |> Array.sumBy (snd >> _.PaymentDue)
                    ActualPaymentTotal = v |> Array.sumBy (snd >> _.ActualPayments >> Array.sumBy ActualPayment.total)
                    GeneratedPaymentTotal = v |> Array.sumBy (snd >> _.GeneratedPayment >> GeneratedPayment.Total)
                    PaymentStatus = v |> Array.head |> snd |> _.PaymentStatus
                |}
            )
            |> Array.filter(fun a -> a.PaymentStatus = MissedPayment || a.PaymentStatus = Underpayment)
            |> Array.choose(fun a ->
                match a.ActualPaymentTotal + a.GeneratedPaymentTotal, a.PaymentDueTotal with
                | NotPaidAtAll ->
                    None
                | SomePaid shortfall ->
                    Some (a.OffsetDay, PaidLaterOwing shortfall)
                | FullyPaid ->
                    Some (a.OffsetDay, PaidLaterInFull)
            )
            |> Map.ofArray
            |> fun m ->
                if m |> Map.isEmpty then
                    schedule
                else
                    schedule
                    |> Map.map(fun d si ->
                        match m |> Map.tryFind d with
                        | Some cps -> { si with PaymentStatus = cps }
                        | None -> si
                    )

        let interestRounding = sp.InterestConfig.InterestRounding

        let chargesHolidays = Charge.holidayDates sp.ChargeConfig sp.StartDate

        let calculateFees appliedPaymentDay originalFinalPaymentDay =
            if originalFinalPaymentDay <= 0<OffsetDay> then
                0L<Cent>
            elif appliedPaymentDay > originalFinalPaymentDay then
                0L<Cent>
            else
                decimal feesTotal * (decimal originalFinalPaymentDay - decimal appliedPaymentDay) / decimal originalFinalPaymentDay |> Cent.round RoundUp

        appliedPayments
        |> Map.toArray
        |> Array.scan(fun ((siOffsetDay, si), a) (appliedPaymentDay, ap) ->

            let window = if ScheduledPayment.isSome ap.ScheduledPayment then si.Window + 1 else si.Window

            let advances = if appliedPaymentDay = 0<OffsetDay> then [| sp.Principal |] else [||] // note: assumes single advance on day 0

            let simpleInterestM =
                if si.PrincipalBalance <= 0L<Cent> then
                    dailyInterestRates siOffsetDay appliedPaymentDay
                    |> Array.map(fun dr -> { dr with InterestRate = sp.InterestConfig.RateOnNegativeBalance })
                    |> Interest.calculate (si.PrincipalBalance + si.FeesBalance) ValueNone interestRounding
                else
                    dailyInterestRates siOffsetDay appliedPaymentDay
                    |> Interest.calculate (si.PrincipalBalance + si.FeesBalance) sp.InterestConfig.Cap.DailyAmount interestRounding

            let cappedSimpleInterestM = if a.CumulativeSimpleInterestM + simpleInterestM >= totalInterestCapM then totalInterestCapM - a.CumulativeSimpleInterestM else simpleInterestM

            let originalSimpleInterestL, contractualInterestM =
                ap.ScheduledPayment.Original
                |> ValueOption.map(fun op -> (op.SimpleInterest, op.ContractualInterest))
                |> ValueOption.defaultValue (0L<Cent>, 0m<Cent>)

            let accumulator =
                { a with
                    CumulativeSimpleInterestM = a.CumulativeSimpleInterestM + cappedSimpleInterestM
                }

            let newInterestM =
                match sp.InterestConfig.Method with
                | Interest.Method.AddOn ->
                    if si.BalanceStatus <> ClosedBalance then
                        a.CumulativeSimpleInterestM + cappedSimpleInterestM
                        |> fun i ->
                            if i > initialInterestBalanceM then
                                i - initialInterestBalanceM
                            else
                                0m<Cent>
                        |> min cappedSimpleInterestM
                    else
                        0m<Cent>
                | Interest.Method.Simple -> simpleInterestM

            let cappedNewInterestM = if a.CumulativeInterest + newInterestM >= totalInterestCapM then totalInterestCapM - a.CumulativeInterest else newInterestM

            let confirmedPayments = ap.ActualPayments |> Array.sumBy(function { ActualPaymentStatus = ActualPaymentStatus.Confirmed ap } -> ap | { ActualPaymentStatus = ActualPaymentStatus.WriteOff ap } -> ap | _ -> 0L<Cent>)

            let pendingPayments = ap.ActualPayments |> Array.sumBy(function { ActualPaymentStatus = ActualPaymentStatus.Pending ap } -> ap | _ -> 0L<Cent>)

            let interestPortionM =
                if confirmedPayments < 0L<Cent> && si.SettlementFigure >= 0L<Cent> then
                    0m<Cent>
                else
                    cappedNewInterestM + si.InterestBalance

            let interestPortionL = interestPortionM |> Cent.fromDecimalCent interestRounding

            let accumulator =
                { accumulator with
                    CumulativeScheduledPayments = a.CumulativeScheduledPayments + ScheduledPayment.total ap.ScheduledPayment
                    CumulativeActualPayments = a.CumulativeActualPayments + confirmedPayments + pendingPayments
                    CumulativeInterest = a.CumulativeInterest + cappedNewInterestM
                }

            let extraPaymentsBalance = a.CumulativeActualPayments - a.CumulativeScheduledPayments - a.CumulativeGeneratedPayments

            let paymentDue =
                if si.BalanceStatus = ClosedBalance || si.BalanceStatus = RefundDue then
                    0L<Cent>
                else
                    match ap.ScheduledPayment.Original, ap.ScheduledPayment.Rescheduled with
                    | _, ValueSome rp -> rp.Value // always make rescheduled payment value due in full
                    | ValueSome op, _ when op.Value = 0L<Cent> -> 0L<Cent> // nothing due to pay
                    | ValueSome op, _ when extraPaymentsBalance > 0L<Cent> -> op.Value - extraPaymentsBalance // reduce the payment due if early/extra payments have been made
                    | ValueSome op, _ -> op.Value // payment due in full
                    | ValueNone, ValueNone -> 0L<Cent> // nothing due to pay
                    |> Cent.min (si.PrincipalBalance + si.FeesBalance + interestPortionL) // payment due should never exceed settlement figure
                    |> Cent.max 0L<Cent> // payment due should never be negative
                |> fun p ->
                    match sp.PaymentConfig.MinimumPayment with
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
                    if chargesHolidays |> Array.exists ((=) (int siOffsetDay)) then
                        0L<Cent>, [||]
                    else
                        Charge.grandTotal sp.ChargeConfig underpayment (ValueSome ap.ChargeTypes), ap.ChargeTypes

            let chargesPortion = newChargesTotal + si.ChargesBalance |> Cent.max 0L<Cent>

            let generatedPayment, netEffect =
                let netEffect = if appliedPaymentDay > asOfDay then Cent.min ap.NetEffect paymentDue else ap.NetEffect
                ap.GeneratedPayment, netEffect

            let sign: int64<Cent> -> int64<Cent> = if netEffect < 0L<Cent> then (( * ) -1L) else id

            let cumulativeSimpleInterestL = accumulator.CumulativeSimpleInterestM |> Cent.fromDecimalCent interestRounding

            let settlement = sp.Principal + cumulativeSimpleInterestL - accumulator.CumulativeActualPayments

            let generatedSettlementPayment, interestAdjustmentM =
                match sp.InterestConfig.Method with
                | Interest.Method.AddOn when si.BalanceStatus <> ClosedBalance ->
                    let interestAdjustment =
                        if (ap.GeneratedPayment = ToBeGenerated || settlement <= 0L<Cent>) && si.BalanceStatus <> RefundDue && cappedNewInterestM = 0m<Cent> then // cappedNewInterest check here avoids adding an interest adjustment twice (one for generated payment, one for final payment)
                            accumulator.CumulativeSimpleInterestM - initialInterestBalanceM
                            |> fun i -> if abs i < 1m<Cent> then 0m<Cent> else i
                            |> fun i -> if accumulator.CumulativeSimpleInterestM + i >= totalInterestCapM then totalInterestCapM - accumulator.CumulativeSimpleInterestM else i
                        else
                            0m<Cent>
                    settlement, interestAdjustment
                | _ ->
                    0L<Cent>, 0m<Cent> // this will be calculated later

            let cappedNewInterestM' = cappedNewInterestM + interestAdjustmentM |> Interest.ignoreFractionalCent

            let interestAdjustmentL = interestAdjustmentM |> Cent.fromDecimalCent interestRounding

            let interestPortionL' = (a.CumulativeInterestPortions, interestPortionL + interestAdjustmentL, Cent.fromDecimalCent interestRounding totalInterestCapM) |> fun (cip, i, tic) -> if cip + i >= tic then tic - cip else i

            let assignable, scheduledPaymentAdjustment =
                if netEffect = 0L<Cent> then
                    0L<Cent>, 0L<Cent>
                else
                    match sp.PaymentConfig.ScheduledPaymentOption with
                    | AsScheduled ->
                        sign netEffect - sign chargesPortion - sign interestPortionL', 0L<Cent>
                    | AddChargesAndInterest ->
                        sign netEffect, sign chargesPortion - sign interestPortionL'

            let scheduledPayment = { ap.ScheduledPayment with Adjustment = scheduledPaymentAdjustment }

            let feesPortion =
                match sp.FeeConfig.FeeAmortisation with
                | Fee.FeeAmortisation.AmortiseBeforePrincipal ->
                    Cent.min si.FeesBalance assignable
                | Fee.FeeAmortisation.AmortiseProportionately ->
                    feesPercentage
                    |> Percent.toDecimal
                    |> fun m ->
                        if (1m + m) = 0m then 0L<Cent>
                        else decimal assignable * m / (1m + m) |> Cent.round RoundUp |> Cent.max 0L<Cent> |> Cent.min si.FeesBalance

            let feesRefundIfSettled =
                match sp.FeeConfig.SettlementRefund with
                | Fee.SettlementRefund.ProRata ->
                    let originalFinalPaymentDay = sp.ScheduleConfig |> generatePaymentMap sp.StartDate |> Map.keys |> Seq.toArray |> Array.tryLast |> Option.defaultValue 0<OffsetDay>
                    calculateFees appliedPaymentDay originalFinalPaymentDay
                | Fee.SettlementRefund.ProRataRescheduled originalFinalPaymentDay ->
                    calculateFees appliedPaymentDay originalFinalPaymentDay
                | Fee.SettlementRefund.Balance ->
                    a.CumulativeFees
                | Fee.SettlementRefund.Zero ->
                    0L<Cent>

            let feesRefund = Cent.max 0L<Cent> feesRefundIfSettled

            let generatedSettlementPayment' =
                match sp.InterestConfig.Method with
                | Interest.Method.AddOn -> generatedSettlementPayment
                | _ -> si.PrincipalBalance + si.FeesBalance - feesRefund + interestPortionL' + chargesPortion

            let feesPortion', feesRefund' =
                if feesPortion > 0L<Cent> && generatedSettlementPayment' <= netEffect then
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

            let carriedCharges, carriedInterestL =
                if sign chargesPortion > sign netEffect then
                    chargesPortion - netEffect, interestPortionL
                elif netEffect = 0L<Cent> && interestPortionM < 0m<Cent> then
                    0L<Cent>, interestPortionL
                elif sign chargesPortion + sign interestPortionL' > sign netEffect then
                    0L<Cent>, interestPortionL - (netEffect - chargesPortion)
                else
                    0L<Cent>, 0L<Cent>

            let offsetDate = sp.StartDate.AddDays(int appliedPaymentDay)

            let balanceStatus = getBalanceStatus principalBalance'

            let interestBalanceM = si.InterestBalance + cappedNewInterestM' - Cent.toDecimalCent (interestPortionL' - carriedInterestL)

            let interestBalanceL = interestBalanceM |> decimal |> Cent.round interestRounding

            let settlementItem () =

                let paymentStatus =
                    match si.BalanceStatus with
                    | ClosedBalance ->
                        NoLongerRequired
                    | _ ->
                        Generated

                let settlementFigure =
                    match sp.InterestConfig.Method with
                    | Interest.Method.AddOn ->
                        generatedSettlementPayment'
                    | _ ->
                        generatedSettlementPayment' - netEffect'

                let generatedPayment' =
                    match generatedPayment with
                    | ToBeGenerated ->
                        GeneratedValue settlementFigure
                    | gp ->
                        gp

                let scheduleItem = {
                    Window = window
                    OffsetDate = offsetDate
                    Advances = advances
                    ScheduledPayment = scheduledPayment
                    PaymentDue = paymentDue'
                    ActualPayments = ap.ActualPayments
                    GeneratedPayment = generatedPayment'
                    NetEffect = netEffect + GeneratedPayment.Total generatedPayment'
                    PaymentStatus = paymentStatus
                    BalanceStatus = ClosedBalance
                    OriginalSimpleInterest = originalSimpleInterestL
                    ContractualInterest = contractualInterestM
                    SimpleInterest = cappedSimpleInterestM
                    NewInterest = cappedNewInterestM'
                    NewCharges = incurredCharges
                    PrincipalPortion = si.PrincipalBalance
                    FeesPortion = si.FeesBalance - feesRefund
                    InterestPortion = interestPortionL'
                    ChargesPortion = chargesPortion
                    FeesRefund = feesRefund
                    PrincipalBalance = 0L<Cent>
                    FeesBalance = 0L<Cent>
                    InterestBalance = 0m<Cent>
                    ChargesBalance = 0L<Cent>
                    SettlementFigure = settlementFigure
                    FeesRefundIfSettled = feesRefundIfSettled
                }
                
                appliedPaymentDay, scheduleItem, settlementFigure, 0m<Cent>

            let nonSettlementItem () =

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
                            if ap.PaymentStatus <> InformationOnly && paymentDue' = 0L<Cent> && confirmedPayments = 0L<Cent> && pendingPayments = 0L<Cent> && GeneratedPayment.Total generatedPayment = 0L<Cent> then
                                NothingDue
                            else
                                ap.PaymentStatus

                    let settlementFigure =
                        match pendingPayments, ap.PaymentStatus, sp.InterestConfig.Method with
                        | pp, _, _ when pp > 0L<Cent> ->
                            0L<Cent>
                        | _, NotYetDue, _
                        | _, _, Interest.Method.AddOn ->
                            generatedSettlementPayment'
                        | _, _, _ ->
                            generatedSettlementPayment' - netEffect'

                    let interestPortionL' = interestPortionL' - carriedInterestL

                    let scheduleItem = {
                        Window = window
                        OffsetDate = offsetDate
                        Advances = advances
                        ScheduledPayment = scheduledPayment
                        PaymentDue = paymentDue'
                        ActualPayments = ap.ActualPayments
                        GeneratedPayment = generatedPayment
                        NetEffect = netEffect'
                        PaymentStatus = paymentStatus
                        BalanceStatus = balanceStatus
                        OriginalSimpleInterest = originalSimpleInterestL
                        ContractualInterest = contractualInterestM
                        SimpleInterest = cappedSimpleInterestM
                        NewInterest = cappedNewInterestM'
                        NewCharges = incurredCharges
                        PrincipalPortion = principalPortion'
                        FeesPortion = feesPortion'
                        InterestPortion = interestPortionL'
                        ChargesPortion = chargesPortion - carriedCharges
                        FeesRefund = feesRefund'
                        PrincipalBalance = principalBalance'
                        FeesBalance = si.FeesBalance - feesPortion' - feesRefund'
                        InterestBalance = interestBalanceM |> Interest.ignoreFractionalCent
                        ChargesBalance = si.ChargesBalance + newChargesTotal - chargesPortion + carriedCharges
                        SettlementFigure = settlementFigure
                        FeesRefundIfSettled = if paymentStatus = NoLongerRequired then 0L<Cent> else feesRefundIfSettled
                    }
                    
                    let interestRoundingDifferenceM = if interestPortionL' = 0L<Cent> then 0m<Cent> else interestBalanceM - Cent.toDecimalCent interestBalanceL

                    appliedPaymentDay, scheduleItem, 0L<Cent>, interestRoundingDifferenceM

            let offsetDay, scheduleItem, generatedPayment, interestRoundingDifferenceM =
                match ap.GeneratedPayment, intendedPurpose with
                | ToBeGenerated, IntendedPurpose.SettlementOnAsOfDay when asOfDay = appliedPaymentDay ->
                    settlementItem ()
                | ToBeGenerated, IntendedPurpose.SettlementOn day when day = appliedPaymentDay ->
                    settlementItem ()
                | GeneratedValue gv, _ ->
                    failwith $"Unexpected value: {gv}"
                | NoGeneratedPayment, _
                | ToBeGenerated, IntendedPurpose.Statement
                | ToBeGenerated, IntendedPurpose.SettlementOn _
                | ToBeGenerated, IntendedPurpose.SettlementOnAsOfDay ->
                    nonSettlementItem ()

            let accumulator' =
                { accumulator with
                    CumulativeScheduledPayments = accumulator.CumulativeScheduledPayments + scheduledPaymentAdjustment
                    CumulativeGeneratedPayments = a.CumulativeGeneratedPayments + generatedPayment
                    CumulativeFees = a.CumulativeFees + feesPortion'
                    CumulativeInterest = accumulator.CumulativeInterest - interestRoundingDifferenceM
                    CumulativeInterestPortions = a.CumulativeInterestPortions + scheduleItem.InterestPortion
                }

            (offsetDay, scheduleItem), accumulator'

        ) (
            (0<OffsetDay>,
            { ScheduleItem.initial with
                OffsetDate = sp.StartDate
                Advances = [| sp.Principal |]
                PrincipalBalance = sp.Principal
                FeesBalance = feesTotal
                InterestBalance = initialInterestBalanceM
                SettlementFigure = sp.Principal + feesTotal
                FeesRefundIfSettled = match sp.FeeConfig.SettlementRefund with Fee.SettlementRefund.Zero -> 0L<Cent> | _ -> feesTotal
            }), {
                CumulativeScheduledPayments = 0L<Cent>
                CumulativeActualPayments = 0L<Cent>
                CumulativeGeneratedPayments = 0L<Cent>
                CumulativeFees = 0L<Cent>
                CumulativeInterest = initialInterestBalanceM
                CumulativeInterestPortions = 0L<Cent>
                CumulativeSimpleInterestM = 0m<Cent>
            }
        )
        |> Array.unzip
        |> fst
        |> fun a -> if (a |> Array.filter(fun (siOffsetDay, _) -> siOffsetDay = 0<OffsetDay>) |> Array.length = 2) then a |> Array.tail else a
        |> Map.ofArray
        |> markMissedPaymentsAsLate

    /// wraps the amortisation schedule in some statistics, and optionally calculate the final APR (optional because it can be processor-intensive)
    let calculateStats sp intendedPurpose (items: Map<int<OffsetDay>, ScheduleItem>) =
        let finalItemDay, finalItem = items |> Map.maxKeyValue
        let items' = items |> Map.toArray |> Array.map snd
        let principalTotal = items' |> Array.sumBy _.PrincipalPortion
        let feesTotal = items' |> Array.sumBy _.FeesPortion
        let interestTotal = items' |> Array.sumBy _.InterestPortion
        let chargesTotal = items' |> Array.sumBy _.ChargesPortion
        let feesRefund = finalItem.FeesRefund
        let finalPaymentDay = finalItemDay
        let finalBalanceStatus = finalItem.BalanceStatus
        let finalAprSolution =
            match intendedPurpose with
            | IntendedPurpose.SettlementOn _ | IntendedPurpose.SettlementOnAsOfDay when finalBalanceStatus = ClosedBalance ->
                items'
                |> Array.filter(fun asi -> asi.NetEffect > 0L<Cent>)
                |> Array.map(fun asi -> { TransferType = Apr.TransferType.Payment; TransferDate = asi.OffsetDate; Value = asi.NetEffect } : Apr.Transfer)
                |> Apr.calculate sp.InterestConfig.AprMethod sp.Principal sp.StartDate
                |> ValueSome
            | _ ->
                 ValueNone
        let scheduledPaymentItems = items |> Map.filter(fun _ si -> ScheduledPayment.isSome si.ScheduledPayment)
        {
            ScheduleItems = items
            FinalScheduledPaymentDay = scheduledPaymentItems |> Map.maxKeyValue |> fst
            FinalScheduledPaymentCount = scheduledPaymentItems |> Map.count
            FinalActualPaymentCount = items' |> Array.sumBy(fun asi -> Array.length asi.ActualPayments)
            FinalApr = finalAprSolution |> ValueOption.map(fun s -> s, Apr.toPercent sp.InterestConfig.AprMethod s)
            FinalCostToBorrowingRatio =
                if principalTotal = 0L<Cent> then Percent 0m
                else decimal (feesTotal + interestTotal + chargesTotal) / decimal principalTotal |> Percent.fromDecimal |> Percent.round 2
            EffectiveInterestRate =
                if finalPaymentDay = 0<OffsetDay> || principalTotal + feesTotal - feesRefund = 0L<Cent> then 0m
                else (decimal interestTotal / decimal (principalTotal + feesTotal - feesRefund)) / decimal finalPaymentDay
                |> Percent |> Interest.Rate.Daily
        }

    /// generates an amortisation schedule and final statistics
    let generate sp intendedPurpose trimEnd actualPayments =
        let schedule = PaymentSchedule.calculate BelowZero sp
        let scheduledPayments =
            schedule.Items
            |> Array.filter (_.ScheduledPayment >> ScheduledPayment.isSome)
            |> Array.map(fun si ->
                let originalSimpleInterest, contractualInterest =
                    match sp.InterestConfig.Method with
                    | Interest.Method.AddOn ->
                        si.SimpleInterest, Cent.toDecimalCent si.InterestPortion
                    | _ ->
                        0L<Cent>, 0m<Cent>
                si.Day,
                { si.ScheduledPayment with
                    Original = si.ScheduledPayment.Original |> ValueOption.map(fun op -> { op with SimpleInterest = originalSimpleInterest; ContractualInterest = contractualInterest })
                }
            )
            |> Map.ofArray

        let asOfDay = sp.AsOfDate |> OffsetDay.fromDate sp.StartDate

        scheduledPayments
        |> applyPayments asOfDay intendedPurpose sp.ChargeConfig sp.PaymentConfig.PaymentTimeout actualPayments
        |> calculate sp intendedPurpose schedule.InitialInterestBalance
        |> if trimEnd then Map.filter(fun _ si -> si.PaymentStatus <> NoLongerRequired) else id
        |> calculateStats sp intendedPurpose
