namespace FSharp.Finance.Personal

open CustomerPayments
open Quotes

/// functions for rescheduling payments after an original schedule failed to amortise
module Rescheduling =

    open ArrayExtension
    open Calculation
    open Currency
    open DateDay
    open FeesAndCharges
    open PaymentSchedule
    open ValueOptionCE

    /// the parameters used for setting up additional items for an existing schedule or new items for a new schedule
    [<RequireQualifiedAccess>]
    type RescheduleParameters = {
        /// whether the fees should be pro-rated or due in full in the new schedule
        FeesSettlementRefund: Fees.SettlementRefund
        /// whether the plan is autogenerated or a manual plan provided
        PaymentSchedule: CustomerPaymentSchedule
        /// how to handle interest on negative principal balances
        RateOnNegativeBalance: Interest.Rate voption
        /// any promotional or introductory offers during which a different interest rate is applicable
        PromotionalInterestRates: Interest.PromotionalRate array
        /// any period during which charges are not payable
        ChargesHolidays: DateRange array
        /// whether and when to generate a settlement figure
        IntendedPurpose: IntendedPurpose
    }

    /// take an existing schedule and reschedule the remaining payments e.g. to allow the customer more time to pay
    let reschedule sp (rp: RescheduleParameters) (actualPayments: CustomerPayment array) =
        voption {
            // get the settlement quote
            let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
            // create a new payment schedule either by auto-generating it or using manual payments
            let newPaymentSchedule =
                match rp.PaymentSchedule with
                | RegularSchedule _ ->
                    let schedule = PaymentSchedule.calculate BelowZero { sp with PaymentSchedule = rp.PaymentSchedule }
                    match schedule with
                    | ValueSome s ->
                        s.Items
                        |> Array.filter(fun si -> si.Payment.IsSome)
                        |> Array.map(fun si -> { PaymentDay = si.Day; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Rescheduled si.Payment.Value; Metadata = Map.empty }; ContractualInterest = ValueNone })
                    | ValueNone ->
                        [||]
                | RegularFixedSchedule regularFixedSchedules ->
                    regularFixedSchedules
                    |> Array.map(fun rfs ->
                        UnitPeriod.generatePaymentSchedule rfs.PaymentCount ValueNone UnitPeriod.Direction.Forward rfs.UnitPeriodConfig
                        |> Array.map(fun d -> { PaymentDay = OffsetDay.fromDate sp.StartDate d; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Rescheduled rfs.PaymentAmount; Metadata = Map.empty }; ContractualInterest = ValueNone })
                    )
                    |> Array.concat
                | IrregularSchedule payments ->
                    payments
            // append the new schedule to the old schedule up to the point of settlement
            let oldPaymentSchedule =
                quote.RevisedSchedule.ScheduleItems
                |> Array.filter _.ScheduledPayment.IsSome
                |> Array.filter(fun si -> si.OffsetDate < sp.AsOfDate)
                |> Array.map(fun si -> { PaymentDay = si.OffsetDay; PaymentDetails = ScheduledPayment si.ScheduledPayment; ContractualInterest = ValueSome si.ContractualInterest })
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    PaymentSchedule = [| oldPaymentSchedule; newPaymentSchedule |] |> Array.concat |> IrregularSchedule
                    FeesAndCharges =
                        { sp.FeesAndCharges with
                            FeesSettlementRefund = rp.FeesSettlementRefund
                            ChargesHolidays = rp.ChargesHolidays
                        }
                    Interest =
                        { sp.Interest with
                            InitialGracePeriod = 0<DurationDay>
                            PromotionalRates = rp.PromotionalInterestRates
                            RateOnNegativeBalance = rp.RateOnNegativeBalance
                        }
                }
            // create the new amortiation schedule
            let! rescheduledSchedule = Amortisation.generate spNew rp.IntendedPurpose ScheduleType.Rescheduled true actualPayments
            return quote.RevisedSchedule, rescheduledSchedule
        }

    /// parameters for creating a rolled-over schedule
    [<RequireQualifiedAccess>]
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
        FeeHandling: Fees.FeeHandling
    }
        /// take an existing schedule and settle it, then use the result to create a new schedule to pay it off under different terms
    let rollOver sp (rp: RolloverParameters) (actualPayments: CustomerPayment array) =
        voption {
            // get the settlement quote
            let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
            // process the quote and extract the portions if applicable
            let! principalPortion, feesPortion, feesRefundIfSettled =
                match quote.QuoteResult with
                | PaymentQuote (paymentQuote, ofWhichPrincipal, ofWhichFees, ofWhichInterest, ofWhichCharges, feesRefundIfSettled) ->
                    match rp.FeeHandling with
                    | Fees.FeeHandling.CapitaliseAsPrincipal -> ofWhichPrincipal + ofWhichFees + ofWhichInterest + ofWhichCharges, 0L<Cent>, feesRefundIfSettled
                    | Fees.FeeHandling.CarryOverAsIs -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, feesRefundIfSettled
                    | Fees.FeeHandling.WriteOffFeeBalance -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, feesRefundIfSettled
                    |> ValueSome
                | AwaitPaymentConfirmation -> ValueNone
                | UnableToGenerateQuote -> ValueNone
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    StartDate = sp.AsOfDate
                    Principal = principalPortion
                    PaymentSchedule = rp.PaymentSchedule
                    FeesAndCharges =
                        match rp.FeeHandling with
                        | Fees.FeeHandling.CarryOverAsIs ->
                            { sp.FeesAndCharges with
                                Fees = [| Fee.CustomFee ("Rolled-Over Fee", Amount.Simple feesPortion) |]
                                FeesSettlementRefund =
                                    if feesRefundIfSettled = 0L<Cent> then
                                        Fees.SettlementRefund.None
                                    else
                                        match sp.FeesAndCharges.FeesSettlementRefund with
                                        | Fees.SettlementRefund.ProRata _ -> Fees.SettlementRefund.ProRata (ValueSome rp.OriginalFinalPaymentDay)
                                        | _ as fsr -> fsr
                            }
                        | _ ->
                            { sp.FeesAndCharges with Fees = [||]; FeesSettlementRefund = Fees.SettlementRefund.None }
                    Interest = rp.Interest |> ValueOption.defaultValue sp.Interest
                    Calculation = rp.Calculation |> ValueOption.defaultValue sp.Calculation
                }
            // create the new amortisation schedule
            let! rescheduledSchedule = Amortisation.generate spNew IntendedPurpose.Statement ScheduleType.Original true [||] // sic: `ScheduledPaymentType.Original` is correct here as this is a new schedule
            return quote.RevisedSchedule, rescheduledSchedule
        }
