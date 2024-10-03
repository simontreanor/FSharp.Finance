namespace FSharp.Finance.Personal

open PaymentSchedule
open Quotes

/// functions for rescheduling payments after an original schedule failed to amortise
module Rescheduling =

    open ArrayExtension
    open Calculation
    open Currency
    open DateDay
    open ValueOptionCE

    let mergeScheduledPayments rescheduleDay (scheduledPayments: (int<OffsetDay> * ScheduledPayment) array) =
        scheduledPayments
        |> Array.groupBy fst
        |> Array.map(fun (offsetDay, scheduledPayments) -> // for each day, there will only ever be a maximum of one original and one rescheduled payment
            let original = scheduledPayments |> Array.tryFind (snd >> _.Original.IsSome) |> toValueOption |> ValueOption.map snd
            let rescheduled = scheduledPayments |> Array.tryFind (snd >> _.Rescheduled.IsSome) |> toValueOption |> ValueOption.map snd
            let scheduledPayment =
                match original, rescheduled with
                | _, ValueSome r ->
                    {
                        Original = original |> ValueOption.bind _.Original
                        Rescheduled = r.Rescheduled
                        Adjustment = r.Adjustment
                        Metadata = r.Metadata
                    }
                | ValueSome o, ValueNone ->
                    {
                        Original = o.Original
                        Rescheduled = if offsetDay >= rescheduleDay then ValueSome 0L<Cent> else ValueNone //overwrite original scheduled payments from start of rescheduled payments
                        Adjustment = o.Adjustment
                        Metadata = o.Metadata
                    }
                | ValueNone, ValueNone ->
                    ScheduledPayment.DefaultValue
            offsetDay, scheduledPayment
        )
        |> Array.filter (snd >> _.IsSome)
        |> Map.ofArray

    /// the parameters used for setting up additional items for an existing schedule or new items for a new schedule
    [<RequireQualifiedAccess>]
    type RescheduleParameters = {
        /// which day the reschedule takes effect: existing payments after this day will be cancelled or replaced by rescheduled payments
        RescheduleDay: int<OffsetDay>
        /// whether the fees should be pro-rated or due in full in the new schedule
        FeeSettlementRefund: Fee.SettlementRefund
        /// whether the plan is autogenerated or a manual plan provided
        PaymentSchedule: ScheduleConfig
        /// how to handle interest on negative principal balances
        RateOnNegativeBalance: Interest.Rate voption
        /// any promotional or introductory offers during which a different interest rate is applicable
        PromotionalInterestRates: Interest.PromotionalRate array
        /// any period during which charges are not payable
        ChargeHolidays: DateRange array
        /// whether and when to generate a settlement figure
        IntendedPurpose: IntendedPurpose
    }

    /// take an existing schedule and reschedule the remaining payments e.g. to allow the customer more time to pay
    let reschedule sp (rp: RescheduleParameters) actualPayments =
        voption {
            // get the settlement quote
            let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
            // create a new payment schedule either by auto-generating it or using manual payments
            let newPaymentSchedule =
                match rp.PaymentSchedule with
                | AutoGenerateSchedule _ ->
                    let schedule = PaymentSchedule.calculate BelowZero { sp with ScheduleConfig = rp.PaymentSchedule }
                    match schedule with
                    | ValueSome s ->
                        s.Items
                        |> Array.filter _.ScheduledPayment.IsSome
                        |> Array.map(fun si -> si.Day, si.ScheduledPayment.Value)
                    | ValueNone ->
                        [||]
                | FixedSchedules regularFixedSchedules ->
                    regularFixedSchedules
                    |> Array.map(fun rfs ->
                        UnitPeriod.generatePaymentSchedule rfs.PaymentCount ValueNone UnitPeriod.Direction.Forward rfs.UnitPeriodConfig
                        |> Array.map(fun d -> OffsetDay.fromDate sp.StartDate d, ScheduledPayment.Quick ValueNone (ValueSome rfs.PaymentAmount))
                    )
                    |> Array.concat
                | CustomSchedule payments ->
                    payments
                    |> Map.toArray
            // append the new schedule to the old schedule up to the point of settlement
            let oldPaymentSchedule =
                quote.RevisedSchedule.ScheduleItems
                |> Map.filter(fun _ si -> si.ScheduledPayment.IsSome)
                |> Map.map(fun _ si -> si.ScheduledPayment)
                |> Map.toArray
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    ScheduleConfig = [| oldPaymentSchedule; newPaymentSchedule |] |> Array.concat |> mergeScheduledPayments rp.RescheduleDay |> CustomSchedule
                    FeeConfig.SettlementRefund = rp.FeeSettlementRefund
                    ChargeConfig.ChargeHolidays = rp.ChargeHolidays
                    Interest =
                        { sp.Interest with
                            InitialGracePeriod = 0<DurationDay>
                            PromotionalRates = rp.PromotionalInterestRates
                            RateOnNegativeBalance = rp.RateOnNegativeBalance
                        }
                }
            // create the new amortiation schedule
            let! rescheduledSchedule = Amortisation.generate spNew rp.IntendedPurpose true actualPayments
            return quote.RevisedSchedule, rescheduledSchedule
        }

    /// parameters for creating a rolled-over schedule
    [<RequireQualifiedAccess>]
    type RolloverParameters = {
        /// the final payment day of the original schedule
        OriginalFinalPaymentDay: int<OffsetDay>
        /// the scheduled payments or the parameters for generating them
        PaymentSchedule: ScheduleConfig
        /// options relating to interest
        Interest: Interest.Options voption
        /// technical calculation options
        Calculation: PaymentSchedule.Calculation voption
        /// how to handle any outstanding fee balance
        FeeHandling: Fee.FeeHandling
    }

    /// take an existing schedule and settle it, then use the result to create a new schedule to pay it off under different terms
    let rollOver sp (rp: RolloverParameters) actualPayments =
        voption {
            // get the settlement quote
            let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
            // process the quote and extract the portions if applicable
            let! principalPortion, feesPortion, feesRefundIfSettled =
                match quote.QuoteResult with
                | PaymentQuote (_, ofWhichPrincipal, ofWhichFees, ofWhichInterest, ofWhichCharges, feesRefundIfSettled) ->
                    match rp.FeeHandling with
                    | Fee.FeeHandling.CapitaliseAsPrincipal -> ofWhichPrincipal + ofWhichFees + ofWhichInterest + ofWhichCharges, 0L<Cent>, feesRefundIfSettled
                    | Fee.FeeHandling.CarryOverAsIs -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, feesRefundIfSettled
                    | Fee.FeeHandling.WriteOffFeeBalance -> ofWhichPrincipal + ofWhichInterest + ofWhichCharges, ofWhichFees, feesRefundIfSettled
                    |> ValueSome
                | AwaitPaymentConfirmation -> ValueNone
                | UnableToGenerateQuote -> ValueNone
            // configure the parameters for the new schedule
            let spNew =
                { sp with
                    StartDate = sp.AsOfDate
                    Principal = principalPortion
                    ScheduleConfig = rp.PaymentSchedule
                    FeeConfig =
                        match rp.FeeHandling with
                        | Fee.FeeHandling.CarryOverAsIs ->
                            { sp.FeeConfig with
                                FeeTypes = [| Fee.CustomFee ("Rollover Fee", Amount.Simple feesPortion) |]
                                SettlementRefund =
                                    if feesRefundIfSettled = 0L<Cent> then
                                        Fee.SettlementRefund.None
                                    else
                                        match sp.FeeConfig.SettlementRefund with
                                        | Fee.SettlementRefund.ProRata _ -> Fee.SettlementRefund.ProRata (ValueSome rp.OriginalFinalPaymentDay)
                                        | _ as fsr -> fsr
                            }
                        | _ ->
                            { sp.FeeConfig with
                                FeeTypes = [||]
                                SettlementRefund = Fee.SettlementRefund.None
                            }
                    Interest = rp.Interest |> ValueOption.defaultValue sp.Interest
                    Calculation = rp.Calculation |> ValueOption.defaultValue sp.Calculation
                }
            // create the new amortisation schedule
            let! rescheduledSchedule = Amortisation.generate spNew IntendedPurpose.Statement true Map.empty
            return quote.RevisedSchedule, rescheduledSchedule
        }
