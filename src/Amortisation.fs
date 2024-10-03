namespace FSharp.Finance.Personal

/// calculating the principal balance over time, taking into account the effects of charges, interest and fees
module Amortisation =

    open ArrayExtension
    open AppliedPayment
    open Calculation
    open Currency
    open DateDay
    open FeesAndCharges
    open PaymentSchedule
    open Percentages
    open ValueOptionCE

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
        GeneratedPayment: int64<Cent> voption
        /// the net effect of the scheduled and actual payments, or, for future days, what the net effect would be if the scheduled payment was actually made
        NetEffect: int64<Cent>
        /// the status based on the payments and net effect
        PaymentStatus: CustomerPaymentStatus
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
    with
        static member DefaultValue = {
            Window = 0
            OffsetDate = Unchecked.defaultof<Date>
            Advances = [||]
            ScheduledPayment = ScheduledPayment.DefaultValue
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueNone
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
        /// the total of interest adjustments up to the current day
        CumulativeInterestAdjustments: decimal<Cent>
        /// the total of interest portions up to the current day
        CumulativeInterestPortions: int64<Cent>
        /// the total of simple interest accrued up to the current day
        CumulativeSimpleInterest: decimal<Cent>
        /// the total of contractual interest up to the current day
        CumulativeContractualInterest: decimal<Cent>
        /// the total of original simple interest accrued up to the current day
        CumulativeOriginalSimpleInterest: int64<Cent>
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
    let internal calculate sp intendedPurpose (initialInterestBalance: int64<Cent>) (appliedPayments: Map<int<OffsetDay>, AppliedPayment>) =
        let asOfDay = (sp.AsOfDate - sp.StartDate).Days * 1<OffsetDay>

        let initialInterestBalance' = initialInterestBalance |> Cent.toDecimalCent

        let totalInterestCap = sp.Interest.Cap.Total |> Interest.Cap.total sp.Principal

        let feesTotal = Fee.GrandTotal sp.FeeConfig sp.Principal ValueNone

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

        let earlySettlementDate = match intendedPurpose with IntendedPurpose.Settlement _ -> ValueSome sp.AsOfDate | _ -> ValueNone

        let isWithinGracePeriod d = int d <= int sp.Interest.InitialGracePeriod

        let isSettledWithinGracePeriod =
            earlySettlementDate
            |> ValueOption.map ((OffsetDay.fromDate sp.StartDate) >> int >> isWithinGracePeriod)
            |> ValueOption.defaultValue false

        let dailyInterestRates fromDay toDay = Interest.dailyRates sp.StartDate isSettledWithinGracePeriod sp.Interest.StandardRate sp.Interest.PromotionalRates fromDay toDay

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
                    ActualPaymentTotal = v |> Array.sumBy (snd >> _.ActualPayments >> Array.sumBy ActualPayment.Total)
                    GeneratedPaymentTotal = v |> Array.sumBy (snd >> _.GeneratedPayment >> (ValueOption.defaultValue 0L<Cent>))
                    PaymentStatus = v |> Array.head |> snd |> _.PaymentStatus
                |}
            )
            |> Array.filter(fun a -> a.PaymentStatus = MissedPayment || a.PaymentStatus = Underpayment)
            |> Array.choose(fun a ->
                match a.ActualPaymentTotal + a.GeneratedPaymentTotal, a.PaymentDueTotal with
                | NotPaidAtAll -> None
                | SomePaid shortfall -> Some (a.OffsetDay, PaidLaterOwing shortfall)
                | FullyPaid -> Some (a.OffsetDay, PaidLaterInFull)
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

        let interestRounding = ValueSome sp.Calculation.RoundingOptions.InterestRounding

        let chargesHolidays = Charge.HolidayDates sp.ChargeConfig sp.StartDate

        appliedPayments
        |> Map.toArray
        |> Array.scan(fun ((siOffsetDay, si), a) (appliedPaymentDay, ap) ->

            let window = if ap.ScheduledPayment.IsSome then si.Window + 1 else si.Window

            let advances = if appliedPaymentDay = 0<OffsetDay> then [| sp.Principal |] else [||] // note: assumes single advance on day 0

            let simpleInterest =
                if si.PrincipalBalance <= 0L<Cent> then
                    match sp.Interest.RateOnNegativeBalance with
                    | ValueSome _ ->
                        dailyInterestRates siOffsetDay appliedPaymentDay
                        |> Array.map(fun dr -> { dr with InterestRate = sp.Interest.RateOnNegativeBalance |> ValueOption.defaultValue Interest.Rate.Zero })
                        |> Interest.calculate (si.PrincipalBalance + si.FeesBalance) ValueNone interestRounding
                    | ValueNone ->
                        0m<Cent>
                else
                    dailyInterestRates siOffsetDay appliedPaymentDay
                    |> Interest.calculate (si.PrincipalBalance + si.FeesBalance) sp.Interest.Cap.Daily interestRounding

            let cappedSimpleInterest = if a.CumulativeSimpleInterest + simpleInterest >= totalInterestCap then totalInterestCap - a.CumulativeSimpleInterest else simpleInterest

            let originalSimpleInterest, contractualInterest = ap.ScheduledPayment.Original |> ValueOption.map(fun o -> (o.SimpleInterest, o.ContractualInterest)) |> ValueOption.defaultValue (0L<Cent>, 0m<Cent>)

            let accumulator =
                { a with
                    CumulativeSimpleInterest = a.CumulativeSimpleInterest + cappedSimpleInterest
                    CumulativeContractualInterest = a.CumulativeContractualInterest + contractualInterest
                }

            let newInterest =
                match sp.Interest.Method with
                | Interest.Method.AddOn ->
                    if si.BalanceStatus <> ClosedBalance then
                        a.CumulativeSimpleInterest + cappedSimpleInterest
                        |> fun i ->
                            if i > initialInterestBalance' then
                                i - initialInterestBalance'
                            else
                                0m<Cent>
                        |> min cappedSimpleInterest
                    else
                        0m<Cent>
                | Interest.Method.Simple -> simpleInterest

            let cappedNewInterest = if a.CumulativeInterest + newInterest >= totalInterestCap then totalInterestCap - a.CumulativeInterest else newInterest

            let confirmedPayments = ap.ActualPayments |> Array.sumBy(function { ActualPaymentStatus = ActualPaymentStatus.Confirmed ap } -> ap | { ActualPaymentStatus = ActualPaymentStatus.WriteOff ap } -> ap | _ -> 0L<Cent>)

            let pendingPayments = ap.ActualPayments |> Array.sumBy(function { ActualPaymentStatus = ActualPaymentStatus.Pending ap } -> ap | _ -> 0L<Cent>)

            let interestPortion =
                if confirmedPayments < 0L<Cent> && si.SettlementFigure >= 0L<Cent> then
                    0m<Cent>
                else
                    cappedNewInterest + si.InterestBalance

            let roundedInterestPortion = interestPortion |> Cent.fromDecimalCent interestRounding

            let accumulator =
                { accumulator with
                    CumulativeScheduledPayments = a.CumulativeScheduledPayments + ap.ScheduledPayment.Total
                    CumulativeActualPayments = a.CumulativeActualPayments + confirmedPayments + pendingPayments
                    CumulativeInterest = a.CumulativeInterest + cappedNewInterest
                }

            let extraPaymentsBalance = a.CumulativeActualPayments - a.CumulativeScheduledPayments - a.CumulativeGeneratedPayments

            let paymentDue =
                if si.BalanceStatus = ClosedBalance || si.BalanceStatus = RefundDue then
                    0L<Cent>
                else
                    match ap.ScheduledPayment.Total with
                    | 0L<Cent> -> 0L<Cent>
                    | amount when extraPaymentsBalance > 0L<Cent> -> amount - extraPaymentsBalance
                    | amount -> Cent.min (si.PrincipalBalance + si.FeesBalance + roundedInterestPortion) amount
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
                    if chargesHolidays |> Array.exists ((=) (int siOffsetDay)) then
                        0L<Cent>, [||]
                    else
                        Charge.GrandTotal sp.ChargeConfig underpayment (ValueSome ap.ChargeTypes), ap.ChargeTypes

            let chargesPortion = newChargesTotal + si.ChargesBalance |> Cent.max 0L<Cent>

            let generatedPayment, netEffect =
                let netEffect = if appliedPaymentDay > asOfDay then Cent.min ap.NetEffect paymentDue else ap.NetEffect
                ap.GeneratedPayment, netEffect

            let sign: int64<Cent> -> int64<Cent> = if netEffect < 0L<Cent> then (( * ) -1L) else id

            let roundedCumulativeSimpleInterest = accumulator.CumulativeSimpleInterest |> Cent.fromDecimalCent interestRounding

            let settlement = sp.Principal + roundedCumulativeSimpleInterest - accumulator.CumulativeActualPayments

            let generatedSettlementPayment, interestAdjustment =
                match sp.Interest.Method with
                | Interest.Method.AddOn when si.BalanceStatus <> ClosedBalance ->
                    let interestAdjustment =
                        if (ap.GeneratedPayment.IsSome || settlement <= 0L<Cent>) && si.BalanceStatus <> RefundDue && cappedNewInterest = 0m<Cent> then // cappedNewInterest check here avoids adding an interest adjustment twice (one for generated payment, one for final payment)
                            accumulator.CumulativeSimpleInterest - (initialInterestBalance |> Cent.toDecimalCent)
                            |> fun i -> if abs i < 1m<Cent> then 0m<Cent> else i
                            |> fun i -> if Cent.toDecimalCent roundedCumulativeSimpleInterest + i >= totalInterestCap then totalInterestCap - Cent.toDecimalCent roundedCumulativeSimpleInterest else i
                        else
                            0m<Cent>
                    settlement, interestAdjustment
                | _ ->
                    0L<Cent>, 0m<Cent> // this will be calculated later

            let cappedNewInterest' = cappedNewInterest + interestAdjustment |> fun i -> if abs i < 1m<Cent> then 0m<Cent> else i
            let roundedInterestAdjustment = interestAdjustment |> Cent.fromDecimalCent interestRounding
            let roundedInterestPortion' = (a.CumulativeInterestPortions, roundedInterestPortion + roundedInterestAdjustment, Cent.fromDecimalCent interestRounding totalInterestCap) |> fun (cip, i, tic) -> if cip + i >= tic then tic - cip else i

            let assignable, scheduledPaymentAmendment =
                if netEffect = 0L<Cent> then
                    0L<Cent>, ValueNone
                else
                    match sp.PaymentOptions.ScheduledPaymentOption with
                    | AsScheduled ->
                        sign netEffect - sign chargesPortion - sign roundedInterestPortion', ValueNone
                    | AddChargesAndInterest ->
                        sign netEffect, ValueSome <| sign chargesPortion - sign roundedInterestPortion'

            let scheduledPayment = { ap.ScheduledPayment with Adjustment = scheduledPaymentAmendment }

            let feesPortion =
                match sp.FeeConfig.FeeAmortisation with
                | Fee.FeeAmortisation.AmortiseBeforePrincipal ->
                    Cent.min si.FeesBalance assignable
                | Fee.FeeAmortisation.AmortiseProportionately ->
                    feesPercentage
                    |> Percent.toDecimal
                    |> fun m ->
                        if (1m + m) = 0m then 0L<Cent>
                        else decimal assignable * m / (1m + m) |> Cent.round (ValueSome RoundUp) |> Cent.max 0L<Cent> |> Cent.min si.FeesBalance

            let calculateFees originalFinalPaymentDay =
                if originalFinalPaymentDay <= 0<OffsetDay> then
                    0L<Cent>
                elif appliedPaymentDay > originalFinalPaymentDay then
                    0L<Cent>
                else
                    decimal feesTotal * (decimal originalFinalPaymentDay - decimal appliedPaymentDay) / decimal originalFinalPaymentDay |> Cent.round (ValueSome RoundUp)

            let feesRefundIfSettled =
                match sp.FeeConfig.SettlementRefund with
                | Fee.SettlementRefund.ProRata originalFinalPaymentDay ->
                    match originalFinalPaymentDay with
                    | ValueNone ->
                        let originalFinalPaymentDay = sp.ScheduleConfig |> generatePaymentMap sp.StartDate |> Map.keys |> Seq.toArray |> Array.tryLast |> toValueOption |> ValueOption.defaultValue 0<OffsetDay>
                        calculateFees originalFinalPaymentDay
                    | ValueSome ofpd ->
                        calculateFees ofpd
                | Fee.SettlementRefund.Balance ->
                    a.CumulativeFees
                | Fee.SettlementRefund.None ->
                    0L<Cent>

            let feesRefund = Cent.max 0L<Cent> feesRefundIfSettled

            let generatedSettlementPayment' =
                match sp.Interest.Method with
                | Interest.Method.AddOn -> generatedSettlementPayment
                | _ -> si.PrincipalBalance + si.FeesBalance - feesRefund + roundedInterestPortion' + chargesPortion

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

            let carriedCharges, carriedInterest =
                if sign chargesPortion > sign netEffect then
                    chargesPortion - netEffect, interestPortion
                elif netEffect = 0L<Cent> && interestPortion < 0m<Cent> then
                    0L<Cent>, interestPortion
                elif sign chargesPortion + sign roundedInterestPortion' > sign netEffect then
                    0L<Cent>, (interestPortion - Cent.toDecimalCent (netEffect - chargesPortion))
                else
                    0L<Cent>, 0m<Cent>

            let roundedCarriedInterest = carriedInterest |> Cent.fromDecimalCent interestRounding

            let offsetDate = sp.StartDate.AddDays(int appliedPaymentDay)

            let balanceStatus = getBalanceStatus principalBalance'

            let interestBalance = si.InterestBalance + cappedNewInterest' - Cent.toDecimalCent (roundedInterestPortion' - roundedCarriedInterest)
            let roundedInterestBalance = interestBalance |> decimal |> Cent.round interestRounding

            let settlementItem () =
                let paymentStatus =
                    match si.BalanceStatus with
                    | ClosedBalance ->
                        NoLongerRequired
                    | _ ->
                        Generated

                let generatedSettlementPayment'' = match sp.Interest.Method with Interest.Method.AddOn -> generatedSettlementPayment' | _ -> generatedSettlementPayment' - netEffect'

                appliedPaymentDay, {
                    Window = window
                    OffsetDate = offsetDate
                    Advances = advances
                    ScheduledPayment = scheduledPayment
                    PaymentDue = paymentDue'
                    ActualPayments = ap.ActualPayments
                    GeneratedPayment = ValueSome generatedSettlementPayment''
                    NetEffect = netEffect + generatedSettlementPayment''
                    PaymentStatus = paymentStatus
                    BalanceStatus = ClosedBalance
                    OriginalSimpleInterest = originalSimpleInterest
                    ContractualInterest = contractualInterest
                    SimpleInterest = cappedSimpleInterest
                    NewInterest = cappedNewInterest'
                    NewCharges = incurredCharges
                    PrincipalPortion = si.PrincipalBalance
                    FeesPortion = si.FeesBalance - feesRefund
                    InterestPortion = roundedInterestPortion'
                    ChargesPortion = chargesPortion
                    FeesRefund = feesRefund
                    PrincipalBalance = 0L<Cent>
                    FeesBalance = 0L<Cent>
                    InterestBalance = 0m<Cent>
                    ChargesBalance = 0L<Cent>
                    SettlementFigure = generatedSettlementPayment''
                    FeesRefundIfSettled = feesRefundIfSettled
                }, generatedSettlementPayment'', 0m<Cent>

            let offsetDay, scheduleItem, generatedPayment, interestRoundingDifference =
                match ap.GeneratedPayment, intendedPurpose with
                | ValueSome _, IntendedPurpose.Settlement ValueNone ->
                    settlementItem ()
                | ValueSome _, IntendedPurpose.Settlement (ValueSome day) when day = appliedPaymentDay ->
                    settlementItem ()

                | ValueNone, _
                | ValueSome _, IntendedPurpose.Statement
                | ValueSome _, IntendedPurpose.Settlement _ ->

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
                            if ap.PaymentStatus <> InformationOnly && paymentDue' = 0L<Cent> && confirmedPayments = 0L<Cent> && pendingPayments = 0L<Cent> && (generatedPayment.IsNone || generatedPayment.Value = 0L<Cent>) then
                                NothingDue
                            else
                                ap.PaymentStatus

                    let generatedSettlementPayment'' =
                        match pendingPayments, ap.PaymentStatus, sp.Interest.Method with
                        | pp, _, _ when pp > 0L<Cent> ->
                            0L<Cent>
                        | _, NotYetDue, _
                        | _, _, Interest.Method.AddOn ->
                            generatedSettlementPayment'
                        | _, _, _ ->
                            generatedSettlementPayment' - netEffect'

                    let interestPortion' = roundedInterestPortion' - roundedCarriedInterest

                    appliedPaymentDay, {
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
                        OriginalSimpleInterest = originalSimpleInterest
                        ContractualInterest = contractualInterest
                        SimpleInterest = cappedSimpleInterest
                        NewInterest = cappedNewInterest'
                        NewCharges = incurredCharges
                        PrincipalPortion = principalPortion'
                        FeesPortion = feesPortion'
                        InterestPortion = interestPortion'
                        ChargesPortion = chargesPortion - carriedCharges
                        FeesRefund = feesRefund'
                        PrincipalBalance = principalBalance'
                        FeesBalance = si.FeesBalance - feesPortion' - feesRefund'
                        InterestBalance = if abs interestBalance < 1m<Cent> then Cent.toDecimalCent roundedInterestBalance else interestBalance
                        ChargesBalance = si.ChargesBalance + newChargesTotal - chargesPortion + carriedCharges
                        SettlementFigure = generatedSettlementPayment''
                        FeesRefundIfSettled = if paymentStatus = NoLongerRequired then 0L<Cent> else feesRefundIfSettled
                    }, 0L<Cent>, if interestPortion' = 0L<Cent> then 0m<Cent> else interestBalance - Cent.toDecimalCent roundedInterestBalance

            let accumulator' =
                { accumulator with
                    CumulativeScheduledPayments = accumulator.CumulativeScheduledPayments + ValueOption.defaultValue 0L<Cent> scheduledPaymentAmendment
                    CumulativeGeneratedPayments = a.CumulativeGeneratedPayments + generatedPayment
                    CumulativeFees = a.CumulativeFees + feesPortion'
                    CumulativeInterest = accumulator.CumulativeInterest - interestRoundingDifference
                    CumulativeInterestAdjustments = accumulator.CumulativeInterestAdjustments + interestAdjustment
                    CumulativeInterestPortions = a.CumulativeInterestPortions + scheduleItem.InterestPortion
                }

            (offsetDay, scheduleItem), accumulator'

        ) (
            (0<OffsetDay>,
            { ScheduleItem.DefaultValue with
                OffsetDate = sp.StartDate
                Advances = [| sp.Principal |]
                PrincipalBalance = sp.Principal
                FeesBalance = feesTotal
                InterestBalance = initialInterestBalance |> Cent.toDecimalCent
                SettlementFigure = sp.Principal + feesTotal
                FeesRefundIfSettled = match sp.FeeConfig.SettlementRefund with Fee.SettlementRefund.None -> 0L<Cent> | _ -> feesTotal
            }), {
                CumulativeScheduledPayments = 0L<Cent>
                CumulativeActualPayments = 0L<Cent>
                CumulativeGeneratedPayments = 0L<Cent>
                CumulativeFees = 0L<Cent>
                CumulativeInterest = initialInterestBalance'
                CumulativeInterestAdjustments = 0m<Cent>
                CumulativeInterestPortions = 0L<Cent>
                CumulativeSimpleInterest = 0m<Cent>
                CumulativeContractualInterest = 0m<Cent>
                CumulativeOriginalSimpleInterest = 0L<Cent>
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
            | IntendedPurpose.Settlement _ when finalBalanceStatus = ClosedBalance ->
                items'
                |> Array.filter(fun asi -> asi.NetEffect > 0L<Cent>)
                |> Array.map(fun asi -> { Apr.TransferType = Apr.Payment; Apr.TransferDate = asi.OffsetDate; Apr.Amount = asi.NetEffect })
                |> Apr.calculate sp.Calculation.AprMethod sp.Principal sp.StartDate
                |> ValueSome
            | _ ->
                 ValueNone
        let scheduledPaymentItems = items |> Map.filter(fun _ si -> si.ScheduledPayment.IsSome)
        {
            ScheduleItems = items
            FinalScheduledPaymentDay = scheduledPaymentItems |> Map.maxKeyValue |> fst
            FinalScheduledPaymentCount = scheduledPaymentItems |> Map.count
            FinalActualPaymentCount = items' |> Array.sumBy(fun asi -> Array.length asi.ActualPayments)
            FinalApr = finalAprSolution |> ValueOption.map(fun s -> s, Apr.toPercent sp.Calculation.AprMethod s)
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
        let scheduledPayments, initialInterestBalance =
            let schedule = PaymentSchedule.calculate BelowZero sp
            match schedule with
            | ValueSome s ->
                s.Items
                |> Array.filter _.ScheduledPayment.IsSome
                |> Array.map(fun si ->
                    let originalSimpleInterest, contractualInterest =
                        match sp.Interest.Method with
                        | Interest.Method.AddOn ->
                            si.SimpleInterest, Cent.toDecimalCent si.InterestPortion
                        | _ ->
                            0L<Cent>, 0m<Cent>
                    si.Day,
                    { si.ScheduledPayment.Value with
                        Original = si.ScheduledPayment.Value.Original |> ValueOption.map(fun op -> { op with SimpleInterest = originalSimpleInterest; ContractualInterest = contractualInterest })
                    }
                )
                |> Map.ofArray
                , s.InitialInterestBalance
            | ValueNone ->
                Map.empty, 0L<Cent>

        if Map.isEmpty scheduledPayments then ValueNone else

        let asOfDay = sp.AsOfDate |> OffsetDay.fromDate sp.StartDate

        scheduledPayments
        |> applyPayments asOfDay intendedPurpose sp.ChargeConfig sp.Calculation.PaymentTimeout actualPayments
        |> calculate sp intendedPurpose initialInterestBalance
        |> if trimEnd then Map.filter(fun _ si -> si.PaymentStatus <> NoLongerRequired) else id
        |> calculateStats sp intendedPurpose
        |> ValueSome
