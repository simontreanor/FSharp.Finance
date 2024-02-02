namespace FSharp.Finance.Personal

module CustomerPayments =

    [<Struct>]
    type GeneratedPayment = // to-do: add other options here
        | SettlementPayment of SettlementPayment:int64<Cent>
        | NextScheduledPayment of NextScheduledPayment:int64<Cent>
        | AllOverduePayment of AllOverduePayment:int64<Cent>

    /// either an extra scheduled payment (e.g. for a restructured payment plan) or an actual payment made, optionally with charges
    [<Struct>]
    type CustomerPaymentDetails =
        /// the amount of any extra scheduled payment due on the current day
        | ScheduledPayment of ScheduledPayment: int64<Cent>
        /// the amounts of any actual payments made on the current day, with any charges incurred
        | ActualPayment of ActualPayment: int64<Cent> * Charges: Charge array
        /// the amounts of any generated payments made on the current day and their type
        | GeneratedPayment of GeneratedPayment
        with
            static member total = function
                | ScheduledPayment sp -> sp
                | ActualPayment (ap, _) -> ap
                | GeneratedPayment gp -> match gp with SettlementPayment p | NextScheduledPayment p | AllOverduePayment p -> p

    /// a payment (either extra scheduled or actually paid) to be applied to a payment schedule
    [<Struct>]
    type CustomerPayment = {
        /// the day the payment is made, as an offset of days from the start date
        PaymentDay: int<OffsetDay>
        /// the details of the payment
        PaymentDetails: CustomerPaymentDetails
    }
 
    /// the status of a payment made by the customer
    [<Struct>]
    type CustomerPaymentStatus =
        /// a scheduled payment was made in full and on time
        | PaymentMade
        /// a scheduled payment was missed completely
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
        | WithinGracePeriod
