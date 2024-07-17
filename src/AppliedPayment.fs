namespace FSharp.Finance.Personal

open CustomerPayments

/// functions for handling received payments and calculating interest and/or charges where necessary
module AppliedPayment =

    open Calculation
    open Currency
    open DateDay
    open FeesAndCharges
    open ValueOptionCE

     /// an actual payment made on a particular day, optionally with charges applied, with the net effect and payment status calculated
    [<Struct>]
    type AppliedPayment = {
        /// the day the payment is made, as an offset of days from the start date
        AppliedPaymentDay: int<OffsetDay>
        /// the amount of any scheduled payment due on the current day
        ScheduledPayment: ScheduledPaymentInfo
        /// the amounts of any actual payments made on the current day
        ActualPayments: ActualPaymentInfo array
        /// a payment generated by the system e.g. to calculate a settlement figure
        GeneratedPayment: int64<Cent> voption
        /// details of any charges incurred on the current day
        IncurredCharges: Charge array
        /// the net effect of any payments made on the current day
        NetEffect: int64<Cent>
        /// the payment status based on the payments made on the current day
        PaymentStatus: CustomerPaymentStatus
    }

    /// groups payments by day, applying actual payments, adding a payment status and optionally a late payment charge if underpaid
    let applyPayments asOfDay intendedPurpose (latePaymentGracePeriod: int<DurationDay>) (latePaymentCharge: Charge voption) chargesGrouping paymentTimeout actualPayments scheduledPayments =
        if Array.isEmpty scheduledPayments then [||] else

        let actualPayments =
            actualPayments
            |> Array.map(fun x ->
                let isTimedOut = x.PaymentDay |> timedOut paymentTimeout asOfDay
                match x.PaymentDetails with
                | ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Pending ap; Metadata = metadata } when isTimedOut -> { x with PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.TimedOut ap; Metadata = metadata } }
                | _ -> x
            )

        let mutable aggregateCharges = Array.empty<Charge>

        let payments =
            [| scheduledPayments; actualPayments |]
            |> Array.concat
            |> Array.groupBy _.PaymentDay
            |> Array.map(fun (offsetDay, payments) ->
                let scheduledPayment' =
                    payments
                    |> Array.tryPick(_.PaymentDetails >> function ScheduledPayment scheduledPaymentInfo -> Some scheduledPaymentInfo | _ -> None)
                    |> Option.defaultValue { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
 
                let actualPayments' = payments |> Array.choose(_.PaymentDetails >> function ActualPayment actualPaymentInfo -> Some actualPaymentInfo | _ -> None)

                let generatedPayment' = payments |> Array.tryPick(_.PaymentDetails >> function GeneratedPayment generatedPayment -> Some generatedPayment | _ -> None) |> toValueOption

                let confirmedPayments =
                    actualPayments'
                    |> Array.choose(function { ActualPaymentStatus = ActualPaymentStatus.Confirmed confirmedAmount } -> Some confirmedAmount | { ActualPaymentStatus = ActualPaymentStatus.WriteOff writeOffAmount } -> Some writeOffAmount | _ -> None)
                    |> Array.sum

                let pendingPayments =
                    actualPayments'
                    |> Array.choose(function { ActualPaymentStatus = ActualPaymentStatus.Pending pendingAmount } -> Some pendingAmount | _ -> None)
                    |> Array.sum

                let netEffect, paymentStatus =
                    if pendingPayments > 0L<Cent> then
                        pendingPayments + confirmedPayments, PaymentPending
                    else
                        match scheduledPayment'.ScheduledPaymentType, confirmedPayments with
                        | ScheduledPaymentType.None, 0L<Cent> ->
                            0L<Cent>, NoneScheduled
                        | ScheduledPaymentType.None, cp when cp < 0L<Cent> ->
                            cp, Refunded
                        | ScheduledPaymentType.None, cp ->
                            cp, ExtraPayment
                        | ScheduledPaymentType.Original sp, cp when cp < sp && offsetDay <= asOfDay && (int offsetDay + int latePaymentGracePeriod) >= int asOfDay ->
                            match intendedPurpose with
                            | IntendedPurpose.Settlement _ when offsetDay < asOfDay ->
                                0L<Cent>, PaymentDue
                            | IntendedPurpose.Settlement _ when offsetDay = asOfDay ->
                                0L<Cent>, Generated
                            | _ ->
                                sp, PaymentDue
                        | sp, _ when offsetDay > asOfDay ->
                            sp.Value, NotYetDue
                        | sp, 0L<Cent> when sp.Value > 0L<Cent> ->
                            0L<Cent>, MissedPayment
                        | sp, cp when cp < sp.Value ->
                            cp, Underpayment
                        | sp, cp when cp > sp.Value ->
                            cp, Overpayment
                        | _, cp -> cp, PaymentMade

                let charges =
                    payments
                    |> Array.collect(_.PaymentDetails >> function (ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (_, c) }) -> c | _ -> [||])
                    |> fun cc ->
                        if latePaymentCharge.IsSome then
                            cc |> Array.append(match paymentStatus with MissedPayment | Underpayment -> [| latePaymentCharge.Value |] | _ -> [||])
                        else cc
                    |> fun cc ->
                        match chargesGrouping with
                        | OneChargeTypePerDay -> cc |> Array.groupBy id |> Array.map fst
                        | OneChargeTypePerProduct ->
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
                        | AllChargesApplied -> cc

                { AppliedPaymentDay = offsetDay; ScheduledPayment = scheduledPayment'; ActualPayments = actualPayments'; GeneratedPayment = generatedPayment'; IncurredCharges = charges; NetEffect = netEffect; PaymentStatus = paymentStatus }
            )

        let appliedPayments day =
            let existingPaymentDay = payments |> Array.tryFind(fun ap -> ap.AppliedPaymentDay = day)
            if existingPaymentDay.IsSome then
                payments
                |> Array.map(fun ap -> if ap.AppliedPaymentDay = day then { ap with GeneratedPayment = ValueSome 0L<Cent> } else ap)
            else
                let newAppliedPayment = {
                    AppliedPaymentDay = day
                    ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
                    ActualPayments = [||]
                    GeneratedPayment = ValueSome 0L<Cent>
                    IncurredCharges = [||]
                    NetEffect = 0L<Cent>
                    PaymentStatus = Generated
                }
                payments
                |> Array.append [| newAppliedPayment |]

        match intendedPurpose with
            | IntendedPurpose.Settlement (ValueSome day) ->
                appliedPayments day
            | IntendedPurpose.Settlement ValueNone ->
                appliedPayments asOfDay
            | IntendedPurpose.Statement ->
                payments
        |> Array.sortBy _.AppliedPaymentDay
