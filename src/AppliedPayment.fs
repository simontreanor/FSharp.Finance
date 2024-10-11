namespace FSharp.Finance.Personal

open PaymentSchedule

/// functions for handling received payments and calculating interest and/or charges where necessary
module AppliedPayment =

    open Calculation
    open Currency
    open DateDay
    open ValueOptionCE

     /// an actual payment made on a particular day, optionally with charges applied, with the net effect and payment status calculated
    [<Struct>]
    type AppliedPayment = {
        /// the amount of any scheduled payment due on the current day
        ScheduledPayment: ScheduledPayment
        /// the amounts of any actual payments made on the current day
        ActualPayments: ActualPayment array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: GeneratedPayment
        /// details of any charges incurred on the current day
        ChargeTypes: Charge.ChargeType array
        /// the net effect of any payments made on the current day
        NetEffect: int64<Cent>
        /// the payment status based on the payments made on the current day
        PaymentStatus: PaymentStatus
    }

    /// groups payments by day, applying actual payments, adding a payment status and optionally a late payment charge if underpaid
    let applyPayments asOfDay intendedPurpose (chargeConfig: Charge.Config) paymentTimeout (actualPayments: Map<int<OffsetDay>, ActualPayment array>) (scheduledPayments: Map<int<OffsetDay>, ScheduledPayment>) =
        if Map.isEmpty scheduledPayments then Map.empty else

        let actualPayments =
            actualPayments
            |> Map.map(fun d app ->
                let isTimedOut = d |> timedOut paymentTimeout asOfDay
                if isTimedOut then
                    app
                    |> Array.map(fun ap ->
                        match ap.ActualPaymentStatus with
                        | ActualPaymentStatus.Pending p -> { ap with ActualPaymentStatus = ActualPaymentStatus.TimedOut p }
                        | _ -> ap
                    )
                else
                    app
            )

        let mutable aggregateCharges = Array.empty<Charge.ChargeType>

        let days = [| scheduledPayments |> Map.keys |> Seq.toArray; actualPayments |> Map.keys |> Seq.toArray |] |> Array.concat |> Array.distinct |> Array.sort

        let appliedPaymentMap =
            days
            |> Array.map(fun offsetDay ->
                let scheduledPayment' =
                    scheduledPayments
                    |> Map.tryFind offsetDay
                    |> Option.defaultValue ScheduledPayment.DefaultValue
 
                let actualPayments' =
                    actualPayments
                    |> Map.tryFind offsetDay
                    |> Option.defaultValue [||]

                let confirmedPayments =
                    actualPayments'
                    |> Array.choose(fun ap -> match ap.ActualPaymentStatus with ActualPaymentStatus.Confirmed confirmedValue -> Some confirmedValue | ActualPaymentStatus.WriteOff writeOffValue -> Some writeOffValue | _ -> None)
                    |> Array.sum

                let pendingPayments =
                    actualPayments'
                    |> Array.choose(fun ap -> match ap.ActualPaymentStatus with ActualPaymentStatus.Pending pendingValue -> Some pendingValue | _ -> None)
                    |> Array.sum

                let latePaymentGracePeriod, latePaymentCharge, chargeGrouping =
                    chargeConfig
                    |> fun cc ->
                        let lpc = cc.ChargeTypes |> Array.tryPick(function Charge.LatePayment _ as c -> Some c | _ -> None) |> toValueOption
                        cc.LatePaymentGracePeriod, lpc, cc.ChargeGrouping

                let netEffect, paymentStatus =
                    if pendingPayments > 0L<Cent> then
                        pendingPayments + confirmedPayments, PaymentPending
                    else
                        match scheduledPayment'.Total, confirmedPayments with
                        | 0L<Cent>, 0L<Cent> ->
                            0L<Cent>, NoneScheduled
                        | 0L<Cent>, cp when cp < 0L<Cent> ->
                            cp, Refunded
                        | 0L<Cent>, cp ->
                            cp, ExtraPayment
                        | sp, cp when cp < sp && offsetDay <= asOfDay && (int offsetDay + int latePaymentGracePeriod) >= int asOfDay ->
                            match intendedPurpose with
                            | IntendedPurpose.SettlementOn day when offsetDay < day ->
                                0L<Cent>, PaymentDue
                            | IntendedPurpose.SettlementOn day when offsetDay = day ->
                                0L<Cent>, Generated
                            | IntendedPurpose.SettlementOnAsOfDay when offsetDay < asOfDay ->
                                0L<Cent>, PaymentDue
                            | IntendedPurpose.SettlementOnAsOfDay when offsetDay = asOfDay ->
                                0L<Cent>, Generated
                            | _ ->
                                sp, PaymentDue
                        | sp, _ when offsetDay > asOfDay ->
                            sp, NotYetDue
                        | sp, 0L<Cent> when sp > 0L<Cent> ->
                            0L<Cent>, MissedPayment
                        | sp, cp when cp < sp ->
                            cp, Underpayment
                        | sp, cp when cp > sp ->
                            cp, Overpayment
                        | _, cp -> cp, PaymentMade

                let charges =
                    actualPayments'
                    |> Array.choose(fun ap -> match ap.ActualPaymentStatus with ActualPaymentStatus.Failed (_, charges) -> Some charges | _ -> None)
                    |> Array.concat
                    |> fun cc ->
                        if latePaymentCharge.IsSome then
                            cc |> Array.append(match paymentStatus with MissedPayment | Underpayment -> [| latePaymentCharge.Value |] | _ -> [||])
                        else cc
                    |> fun cc ->
                        match chargeGrouping with
                        | Charge.ChargeGrouping.OneChargeTypePerDay -> cc |> Array.groupBy id |> Array.map fst
                        | Charge.ChargeGrouping.OneChargeTypePerProduct ->
                            cc
                            |> Array.groupBy id
                            |> Array.map fst
                            |> Array.collect(fun c ->
                                match aggregateCharges |> Array.tryFind((=) c) with
                                | Some _ -> [||]
                                | None ->
                                    aggregateCharges <- aggregateCharges |> Array.append [| c |]
                                    [| c |]
                            )
                        | Charge.ChargeGrouping.AllChargesApplied -> cc

                let appliedPayment = {
                    ScheduledPayment = scheduledPayment'
                    ActualPayments = actualPayments'
                    GeneratedPayment = NoGeneratedPayment
                    ChargeTypes = charges
                    NetEffect = netEffect
                    PaymentStatus = paymentStatus
                }

                offsetDay, appliedPayment
            )
            |> Map.ofArray

        let appliedPayments day generatedPayment paymentStatus =
            let existingPaymentDay =
                appliedPaymentMap
                |> Map.tryFind day
            if existingPaymentDay.IsSome then
                appliedPaymentMap
                |> Map.map(fun d ap -> if d = day then { ap with GeneratedPayment = generatedPayment } else ap)
            else
                let newAppliedPayment = {
                    ScheduledPayment = ScheduledPayment.DefaultValue
                    ActualPayments = [||]
                    GeneratedPayment = generatedPayment
                    ChargeTypes = [||]
                    NetEffect = 0L<Cent>
                    PaymentStatus = paymentStatus
                }
                appliedPaymentMap
                |> Map.add day newAppliedPayment

        match intendedPurpose with
            | IntendedPurpose.SettlementOn day ->
                appliedPayments day ToBeGenerated Generated
            | IntendedPurpose.SettlementOnAsOfDay ->
                appliedPayments asOfDay ToBeGenerated Generated
            | IntendedPurpose.Statement ->
                let maxPaymentDay = appliedPaymentMap |> Map.maxKeyValue |> fst
                if asOfDay >= maxPaymentDay then
                    appliedPaymentMap
                else
                    appliedPayments asOfDay NoGeneratedPayment InformationOnly
