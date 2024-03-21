namespace FSharp.Finance.Personal

open CustomerPayments

/// functions for handling received payments and calculating interest and/or charges where necessary
module AppliedPayment =

     /// an actual payment made on a particular day, optionally with charges applied, with the net effect and payment status calculated
    [<Struct>]
    type AppliedPayment = {
        /// the day the payment is made, as an offset of days from the start date
        AppliedPaymentDay: int<OffsetDay>
        /// the amount of any scheduled payment due on the current day
        ScheduledPayment: ScheduledPaymentType
        /// the amounts of any actual payments made on the current day
        ActualPayments: ActualPaymentStatus array
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
    let applyPayments asOfDay intendedPurpose (latePaymentGracePeriod: int<DurationDay>) (latePaymentCharge: Amount voption) chargesGrouping paymentTimeout actualPayments scheduledPayments =
        if Array.isEmpty scheduledPayments then [||] else

        let actualPayments =
            actualPayments
            |> Array.map(fun x ->
                let isTimedOut = x.PaymentDay |> timedOut paymentTimeout asOfDay
                match x.PaymentDetails with
                | ActualPayment (ActualPaymentStatus.Pending ap) when isTimedOut -> { x with PaymentDetails = ActualPayment (ActualPaymentStatus.TimedOut ap) }
                | _ -> x
            )

        let payments =
            [| scheduledPayments; actualPayments |]
            |> Array.concat
            |> Array.groupBy _.PaymentDay
            |> Array.map(fun (offsetDay, payments) ->
                let scheduledPayment' = payments |> Array.tryPick(_.PaymentDetails >> function ScheduledPayment sp -> Some sp | _ -> None) |> function Some sp -> sp | None -> ScheduledPaymentType.None
 
                let actualPayments' = payments |> Array.choose(_.PaymentDetails >> function ActualPayment ap -> Some ap | _ -> None)

                let generatedPayment' = payments |> Array.tryPick(_.PaymentDetails >> function GeneratedPayment gp -> Some gp | _ -> None) |> toValueOption

                let confirmedPayments =
                    actualPayments'
                    |> Array.choose(function ActualPaymentStatus.Confirmed ap -> Some ap | ActualPaymentStatus.WriteOff ap -> Some ap | _ -> None)
                    |> Array.sum

                let pendingPayments =
                    actualPayments'
                    |> Array.choose(function ActualPaymentStatus.Pending ap -> Some ap | _ -> None)
                    |> Array.sum

                let netEffect, paymentStatus =
                    if pendingPayments > 0L<Cent> then
                        pendingPayments + confirmedPayments, PaymentPending
                    else
                        match scheduledPayment', confirmedPayments with
                        | ScheduledPaymentType.None, 0L<Cent> ->
                            0L<Cent>, NoneScheduled
                        | ScheduledPaymentType.None, ap when ap < 0L<Cent> ->
                            ap, Refunded
                        | ScheduledPaymentType.None, ap ->
                            ap, ExtraPayment
                        | ScheduledPaymentType.Original sp, ap when ap < sp && offsetDay <= asOfDay && (int offsetDay + int latePaymentGracePeriod) >= int asOfDay ->
                            match intendedPurpose with
                            | IntendedPurpose.Quote _ when offsetDay < asOfDay ->
                                0L<Cent>, PaymentDue
                            | IntendedPurpose.Quote quoteType when offsetDay = asOfDay ->
                                0L<Cent>, Generated quoteType
                            | _ ->
                                sp, PaymentDue
                        | sp, _ when offsetDay > asOfDay ->
                            sp.Value, NotYetDue
                        | sp, 0L<Cent> when sp.Value > 0L<Cent> ->
                            0L<Cent>, MissedPayment
                        | sp, ap when ap < sp.Value ->
                            ap, Underpayment
                        | sp, ap when ap > sp.Value ->
                            ap, Overpayment
                        | _, ap -> ap, PaymentMade

                let groupCharges =
                    match chargesGrouping with
                    | OneChargeTypePerDay -> Array.groupBy id >> Array.map fst
                    | AllChargesApplied -> id

                let charges =
                    payments
                    |> Array.collect(_.PaymentDetails >> function ActualPayment (ActualPaymentStatus.Failed (_, c)) -> c | _ -> [||])
                    |> groupCharges
                    |> fun pcc ->
                        if latePaymentCharge.IsSome then
                            pcc |> Array.append(match paymentStatus with MissedPayment | Underpayment -> [| Charge.LatePayment latePaymentCharge.Value |] | _ -> [||])
                        else pcc

                { AppliedPaymentDay = offsetDay; ScheduledPayment = scheduledPayment'; ActualPayments = actualPayments'; GeneratedPayment = generatedPayment'; IncurredCharges = charges; NetEffect = netEffect; PaymentStatus = paymentStatus }
            )

        let appliedPayments day quoteType =
            let existingPaymentDay = payments |> Array.tryFind(fun ap -> ap.AppliedPaymentDay = day)
            if existingPaymentDay.IsSome then
                payments
                |> Array.map(fun ap -> if ap.AppliedPaymentDay = day then { ap with GeneratedPayment = ValueSome 0L<Cent> } else ap)
            else
                let newAppliedPayment = {
                    AppliedPaymentDay = day
                    ScheduledPayment = ScheduledPaymentType.None
                    ActualPayments = [||]
                    GeneratedPayment = ValueSome 0L<Cent>
                    IncurredCharges = [||]
                    NetEffect = 0L<Cent>
                    PaymentStatus = Generated quoteType
                }
                payments
                |> Array.append [| newAppliedPayment |]

        match intendedPurpose with
            | IntendedPurpose.Quote (FutureSettlement day) ->
                appliedPayments day (FutureSettlement day)
            | IntendedPurpose.Quote quoteType ->
                appliedPayments asOfDay quoteType
            | IntendedPurpose.Statement ->
                payments
        |> Array.sortBy _.AppliedPaymentDay
