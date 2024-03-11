namespace FSharp.Finance.Personal

open CustomerPayments
open Quotes

/// functions for rescheduling payments after an original schedule failed to amortise
module Rescheduling =

    open Fees

    /// the parameters used for setting up additional items for an existing schedule or new items for a new schedule
    [<RequireQualifiedAccess; Struct>]
    type RescheduleParameters = {
        /// the final payment day of the original schedule
        OriginalFinalPaymentDay: int<OffsetDay>
        /// whether the fees should be pro-rated or due in full in the new schedule
        FeesSettlement: Fees.Settlement
        /// whether the plan is autogenerated or a manual plan provided
        PaymentSchedule: CustomerPaymentSchedule
        /// how to handle interest on negative principal balances
        NegativeInterestOption: NegativeInterestOption
        /// any periods during which interest is not payable
        InterestHolidays: Holiday array
        /// any period during which charges are not payable
        ChargesHolidays: Holiday array
    }

    /// take an existing schedule and settle it, then use the result to create a new schedule to pay it off under different terms
    let reschedule sp (rp: RescheduleParameters) (actualPayments: CustomerPayment array) =
        voption {
            // get the settlement quote
            let! quote = getQuote QuoteType.Settlement sp actualPayments
            // create a new payment schedule either by auto-generating it or using manual payments
            let newPaymentSchedule =
                match rp.PaymentSchedule with
                | RegularSchedule _ ->
                    let schedule = PaymentSchedule.calculate BelowZero { sp with PaymentSchedule = rp.PaymentSchedule }
                    match schedule with
                    | ValueSome s ->
                        s.Items
                        |> Array.filter(fun si -> si.Payment.IsSome)
                        |> Array.map(fun si -> { PaymentDay = si.Day; PaymentDetails = ScheduledPayment si.Payment.Value })
                    | ValueNone ->
                        [||]
                | RegularFixedSchedule (unitPeriodConfig, paymentCount, paymentAmount) ->
                    UnitPeriod.generatePaymentSchedule paymentCount UnitPeriod.Direction.Forward unitPeriodConfig
                    |> Array.map(fun d -> { PaymentDay = OffsetDay.fromDate sp.StartDate d; PaymentDetails = ScheduledPayment paymentAmount })
                | IrregularSchedule payments ->
                    payments
            // append the new schedule to the old schedule up to the point of settlement
            let oldPaymentSchedule =
                quote.RevisedSchedule.ScheduleItems
                |> Array.filter _.ScheduledPayment.IsSome
                |> Array.map(fun si -> { PaymentDay = si.OffsetDay; PaymentDetails = ScheduledPayment si.ScheduledPayment.Value })
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    ScheduleType = PaymentSchedule.Reschedule rp.OriginalFinalPaymentDay
                    PaymentSchedule = [| oldPaymentSchedule; newPaymentSchedule |] |> Array.concat |> IrregularSchedule
                    FeesAndCharges = { sp.FeesAndCharges with ChargesHolidays = rp.ChargesHolidays }
                    Interest = { sp.Interest with InitialGracePeriod = 0<DurationDay>; Holidays = rp.InterestHolidays }
                    Calculation = { sp.Calculation with NegativeInterestOption = rp.NegativeInterestOption }
                }
            // create the new amortiation schedule
            let! rescheduledSchedule = Amortisation.generate spNew IntendedPurpose.Statement [||]
            return quote.RevisedSchedule, rescheduledSchedule
        }

    /// parameters for creating a rolled-over schedule
    [<RequireQualifiedAccess; Struct>]
    type RolloverParameters = {
        /// the final payment day of the original schedule
        OriginalFinalPaymentDay: int<OffsetDay>
        /// the scheduled payments or the parameters for generating them
        PaymentSchedule: CustomerPaymentSchedule
        /// options relating to fees
        FeesAndCharges: FeesAndCharges voption
        /// options relating to interest
        Interest: Interest.Options voption
        /// technical calculation options
        Calculation: PaymentSchedule.Calculation voption
        /// how to handle any outstanding fee balance
        FeeHandling: FeeHandling
    }
        /// take an existing schedule and settle it, then use the result to create a new schedule to pay it off under different terms
    let rollOver sp (rp: RolloverParameters) (actualPayments: CustomerPayment array) =
        voption {
            // get the settlement quote
            let! quote = getQuote QuoteType.Settlement sp actualPayments
            // process the quote and extract the portions if applicable
            let! principalPortion, feesPortion, proRatedFees =
                match quote.QuoteResult with
                | PaymentQuote (paymentQuote, ofWhichPrincipal, ofWhichFees, ofWhichInterest, ofWhichCharges, proRatedFees) ->
                    match rp.FeeHandling with
                    | CapitaliseAsPrincipal -> ofWhichPrincipal + ofWhichFees + ofWhichInterest + ofWhichCharges, 0L<Cent>, proRatedFees
                    | CarryOverAsIs -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, proRatedFees
                    | WriteOffFeeBalance -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, proRatedFees
                    |> ValueSome
                | AwaitPaymentConfirmation -> ValueNone
                | UnableToGenerateQuote -> ValueNone
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    StartDate = sp.AsOfDate
                    ScheduleType = PaymentSchedule.Reschedule rp.OriginalFinalPaymentDay
                    Principal = principalPortion
                    PaymentSchedule = rp.PaymentSchedule
                    FeesAndCharges =
                        match rp.FeeHandling with
                        | CarryOverAsIs ->
                            { sp.FeesAndCharges with
                                Fees = [| Fee.CustomFee ("Rolled-Over Fee", Amount.Simple feesPortion) |]
                                FeesSettlement =
                                    if proRatedFees = 0L<Cent> then DueInFull else
                                    match rp.FeeHandling with
                                    | CapitaliseAsPrincipal -> DueInFull
                                    | CarryOverAsIs -> ProRataRefund
                                    | WriteOffFeeBalance -> DueInFull
                            }
                        | _ ->
                            { sp.FeesAndCharges with Fees = [||]; FeesSettlement = DueInFull }
                    Interest = rp.Interest |> ValueOption.defaultValue sp.Interest
                    Calculation = rp.Calculation |> ValueOption.defaultValue sp.Calculation
                }
            // create the new amortiation schedule
            let! rescheduledSchedule = Amortisation.generate spNew IntendedPurpose.Statement [||]
            return quote.RevisedSchedule, rescheduledSchedule
        }

    /// to-do: create a function to disapply all interest paid so far
    let disapplyInterest =
        ()