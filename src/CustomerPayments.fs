namespace FSharp.Finance.Personal

/// categorising the types of incoming payments based on whether they are scheduled, actual or generated
module CustomerPayments =

    open Calculation
    open Currency
    open DateDay
    open FeesAndCharges

    /// the type of the scheduled payment, affecting how any payment due is calculated
    [<RequireQualifiedAccess; Struct>]
    type ScheduledPaymentType =
        /// no scheduled payment
        | None
        /// the payment due may be reduced by prior extra/overpayments
        | Original of OriginalAmount: int64<Cent>
        /// the payment due remains the same regardless of any prior extra/overpayments (though it may be reduced if the amount is sufficient to close the balance)
        | Rescheduled of RescheduledAmount: int64<Cent>
        with
            /// the amount of the scheduled payment
            member x.Value =
                match x with
                | None -> 0L<Cent>
                | Original sp -> sp
                | Rescheduled sp -> sp
            member x.IsSome =
                match x with
                | None -> false
                | _ -> true

    type ScheduledPaymentInfo =
        {
            ScheduledPaymentType: ScheduledPaymentType
            Metadata: Map<string, obj>
        }
        with
            member x.Value = match x with { ScheduledPaymentType = spt } -> spt.Value
            member x.IsSome = match x with { ScheduledPaymentType = spt } -> spt.IsSome

    /// the status of the payment, allowing for delays due to payment-provider processing times
    [<RequireQualifiedAccess; Struct>]
    type ActualPaymentStatus =
        /// a write-off payment has been applied
        | WriteOff of WriteOffAmount: int64<Cent>
        /// the payment has been initiated but is not yet confirmed
        | Pending of PendingAmount: int64<Cent>
        /// the payment had been initiated but was not confirmed within the timeout
        | TimedOut of TimedOutAmount: int64<Cent>
        /// the payment has been confirmed
        | Confirmed of ConfirmedAmount: int64<Cent>
        /// the payment has been failed, with optional charges (e.g. due to insufficient-funds penalties)
        | Failed of FailedAmount: int64<Cent> * Charges: Charge array
        with
            /// the total amount of the payment
            static member total = function
                | WriteOff ap -> ap
                | Pending ap -> ap
                | TimedOut _ -> 0L<Cent>
                | Confirmed ap -> ap
                | Failed _ -> 0L<Cent>

    type ActualPaymentInfo =
        {
            ActualPaymentStatus: ActualPaymentStatus
            Metadata: Map<string, obj>
        }
        with
            static member total = function { ActualPaymentStatus = aps } -> ActualPaymentStatus.total aps

    /// either an extra scheduled payment (e.g. for a restructured payment plan) or an actual payment made, optionally with charges
    type CustomerPaymentDetails =
        /// the amount of any extra scheduled payment due on the current day
        | ScheduledPayment of ScheduledPayment: ScheduledPaymentInfo
        /// the amounts of any actual payments made on the current day, with any charges incurred
        | ActualPayment of ActualPayment: ActualPaymentInfo
        /// the amounts of any generated payments made on the current day and their type
        | GeneratedPayment of GeneratedPayment: int64<Cent>
        with
            static member total = function
                | ScheduledPayment spi -> spi.Value
                | ActualPayment api -> ActualPaymentStatus.total api.ActualPaymentStatus
                | GeneratedPayment gp -> gp

    /// a payment (either extra scheduled or actually paid) to be applied to a payment schedule
    type CustomerPayment = {
        /// the day the payment is made, as an offset of days from the start date
        PaymentDay: int<OffsetDay>
        /// the details of the payment
        PaymentDetails: CustomerPaymentDetails
        /// the original simple interest
        OriginalSimpleInterest: decimal<Cent> voption
        /// the original, contractually calculated interest
        ContractualInterest: decimal<Cent> voption
     }
        with
            static member ScheduledOriginal paymentDay amount =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original amount; Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
            static member ScheduledRescheduled paymentDay amount =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Rescheduled amount; Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
            static member ActualConfirmed paymentDay amount =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed amount; Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
            static member ActualPending paymentDay amount =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Pending amount; Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
            static member ActualFailed paymentDay amount charges =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (amount, charges); Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
            static member ActualWriteOff paymentDay amount =
                {
                    PaymentDay = paymentDay
                    PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.WriteOff amount; Metadata = Map.empty }
                    OriginalSimpleInterest = ValueNone
                    ContractualInterest = ValueNone
                }
 
    /// the status of a payment made by the customer
    [<Struct>]
    type CustomerPaymentStatus =
        /// no payment is required on the specified day
        | NoneScheduled
        /// a payment has been initiated but not yet confirmed
        | PaymentPending
        /// a scheduled payment was made in full and on time
        | PaymentMade
        /// no payment is due on the specified day because of earlier extra-/overpayments
        | NothingDue
        /// a scheduled payment is not paid on time, but is paid within the window
        | PaidLaterInFull
        /// a scheduled payment is not paid on time, but is partially paid within the window
        | PaidLaterOwing of Shortfall: int64<Cent>
        /// a scheduled payment was missed completely, i.e. not paid within the window
        | MissedPayment
        /// a scheduled payment was made on time but not in the full amount
        | Underpayment
        /// a scheduled payment was made on time but exceeded the full amount
        | Overpayment
        /// a payment was made on a day when no payments were scheduled
        | ExtraPayment
        /// a refund was processed
        | Refunded
        /// a scheduled payment is in the future (seen from the as-of date)
        | NotYetDue
        /// a scheduled payment has not been made on time but is within the late-charge grace period
        | PaymentDue
        /// a payment generated by a settlement quote
        | Generated
        /// no payment needed because the loan has already been settled
        | NoLongerRequired
        /// a schedule item generated to show the balances on the as-of date
        | InformationOnly

    /// a regular schedule based on a unit-period config with a specific number of payments of a specified amount
    [<RequireQualifiedAccess; Struct>]
    type RegularFixedSchedule = {
        UnitPeriodConfig: UnitPeriod.Config
        PaymentCount: int
        PaymentAmount: int64<Cent>
    }

    /// whether a payment plan is generated according to a regular schedule or is an irregular array of payments
    type CustomerPaymentSchedule =
        /// a regular schedule based on a unit-period config with a specific number of payments with an auto-calculated amount
        | RegularSchedule of UnitPeriodConfig: UnitPeriod.Config * PaymentCount: int * MaxDuration: Duration voption
        /// a regular schedule based on one or more unit-period configs each with a specific number of payments of a specified amount
        | RegularFixedSchedule of RegularFixedSchedule array
        /// just a bunch of payments
        | IrregularSchedule of IrregularSchedule: CustomerPayment array
