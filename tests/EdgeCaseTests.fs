namespace FSharp.Finance.Personal.Tests

open Xunit
open FsUnit.Xunit

open FSharp.Finance.Personal

module EdgeCaseTests =

    open Amortisation
    open Calculation
    open Currency
    open CustomerPayments
    open DateDay
    open FeesAndCharges
    open PaymentSchedule
    open Percentages
    open Quotes
    open Rescheduling
    open ValueOptionCE

    let interestCapExample : Interest.Cap = {
        Total = ValueSome (Amount.Percentage (Percent 100m, ValueNone, ValueSome RoundDown))
        Daily = ValueSome (Amount.Percentage (Percent 0.8m, ValueNone, ValueNone))
    }

    [<Fact>]
    let ``1) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2023, 2, 9)
            Principal = 30000L<Cent>
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay = 15<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 137_40L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 43<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 137_40L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 74<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 137_40L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 104<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 137_40L<Cent>; Metadata = Map.empty } }
            |]
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [| Fee.CabOrCsoFee (Amount.Percentage (Percent 154.47m, ValueNone, ValueSome RoundDown)) |]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 3<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Annual (Percent 9.95m)
                Cap = Interest.Cap.none
                InitialGracePeriod = 3<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UsActuarial 5
                RoundingOptions = RoundingOptions.recommended
                MinimumPayment = DeferOrWriteOff 50L<Cent>
                PaymentTimeout = 3<DurationDay>
            }
        }

        let actualPayments = [|
            { PaymentDay = 5<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 31200L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest001.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (500_79L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 48_34L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 274_64L<Cent>; OfWhichPrincipal = 177_81L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``2) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2022, 2, 2)
            Principal = 25000L<Cent>
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay = 16<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 11500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 44<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 11500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 75<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 11500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 105<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 11500L<Cent>; Metadata = Map.empty } }
            |]
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [| Fee.CabOrCsoFee (Amount.Percentage (Percent 154.47m, ValueNone, ValueSome RoundDown)) |]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 3<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Annual (Percent 9.95m)
                Cap = Interest.Cap.none
                InitialGracePeriod = 3<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UsActuarial 5
                RoundingOptions = RoundingOptions.recommended
                MinimumPayment = DeferOrWriteOff 50L<Cent>
                PaymentTimeout = 3<DurationDay>
            }
        }

        let actualPayments = [|
            { PaymentDay = 5<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 26000L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest002.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (455_55L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 78_52L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 228_86L<Cent>; OfWhichPrincipal = 148_17L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``3) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2022, 12, 2)
            Principal = 75000L<Cent>
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay = 14<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 34350L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 45<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 34350L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 76<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 34350L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 104<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 34350L<Cent>; Metadata = Map.empty } }
            |]
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [| Fee.CabOrCsoFee (Amount.Percentage (Percent 154.47m, ValueNone, ValueSome RoundDown)) |]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 3<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Annual (Percent 9.95m)
                Cap = Interest.Cap.none
                InitialGracePeriod = 3<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UsActuarial 5
                RoundingOptions = RoundingOptions.recommended
                MinimumPayment = DeferOrWriteOff 50L<Cent>
                PaymentTimeout = 3<DurationDay>
            }
        }

        let actualPayments = [|
            { PaymentDay = 13<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 82800L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest003.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (1221_54L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 134_26L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 660_00L<Cent>; OfWhichPrincipal = 427_28L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``4) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2020, 10, 8)
            Principal = 50000L<Cent>
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay = 8<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 39<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 69<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 100<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 214<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 25000L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 245<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 27600L<Cent>; Metadata = Map.empty } }
            |]
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [| Fee.CabOrCsoFee (Amount.Percentage (Percent 154.47m, ValueNone, ValueSome RoundDown)) |]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 3<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Annual (Percent 9.95m)
                Cap = Interest.Cap.none
                InitialGracePeriod = 3<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UsActuarial 5
                RoundingOptions = RoundingOptions.recommended
                MinimumPayment = DeferOrWriteOff 50L<Cent>
                PaymentTimeout = 3<DurationDay>
            }
        }

        let actualPayments = [|
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 22500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 24900L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 380<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 407<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 475<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17600L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest004.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (466_41L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 81_43L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 233_66L<Cent>; OfWhichPrincipal = 151_32L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``4a) Only one insufficient funds charge per day`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2020, 10, 8)
            Principal = 50000L<Cent>
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay = 8<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 39<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 69<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 100<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 22500L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 214<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 25000L<Cent>; Metadata = Map.empty } }
                { PaymentDay = 245<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Original 27600L<Cent>; Metadata = Map.empty } }
            |]
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [| Fee.CabOrCsoFee (Amount.Percentage (Percent 154.47m, ValueNone, ValueSome RoundDown)) |]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 3<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Annual (Percent 9.95m)
                Cap = Interest.Cap.none
                InitialGracePeriod = 3<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UsActuarial 5
                RoundingOptions = RoundingOptions.recommended
                MinimumPayment = DeferOrWriteOff 50L<Cent>
                PaymentTimeout = 3<DurationDay>
            }
        }

        let actualPayments = [|
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [| Charge.InsufficientFunds (Amount.Simple 10_00L<Cent>) |]); Metadata = Map.empty } }
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [| Charge.InsufficientFunds (Amount.Simple 10_00L<Cent>) |]); Metadata = Map.empty } }
            { PaymentDay = 8<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [| Charge.InsufficientFunds (Amount.Simple 10_00L<Cent>) |]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 11<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 14<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 39<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 42<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 69<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 22500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 72<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 75<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 100<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 103<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (23700L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 24900L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 106<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (22500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 214<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 217<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 220<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 245<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 248<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 251<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (25000L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 379<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17500L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 380<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 407<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17500L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 435<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 438<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 441<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (17600L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 475<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17600L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest004a.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (479_92L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 83_79L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 240_43L<Cent>; OfWhichPrincipal = 155_70L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``5) Quote returning nothing`` () =
        let sp = {

            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2022, 6, 22)
            Principal = 500_00L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2022, 7, 15), 6, ValueNone)
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [||]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 0<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
                RoundingOptions = RoundingOptions.recommended
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
        }

        let actualPayments = [|
            { PaymentDay = 23<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (166_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 23<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 23<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 26<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 26<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 26<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 29<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 29<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 29<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 54<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 54<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 54<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 57<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 57<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 57<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 60<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 60<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 60<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 85<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 85<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 85<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 88<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 88<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 88<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 91<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 91<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 91<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (66_67L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 135<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 83_33L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 165<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 83_33L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 196<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 196<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 196<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 199<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 199<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 199<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 202<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 202<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 202<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 227<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 227<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 227<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 230<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 230<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 230<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 233<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 233<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 233<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 255<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 255<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 255<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 258<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 258<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 258<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 261<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 261<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 261<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 286<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 286<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 286<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 289<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 289<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 289<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 292<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 292<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 292<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (83_33L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 322<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 353<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 384<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 408<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 449<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 476<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 17_58L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 499<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 15_74L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 531<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 15_74L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 574<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 15_74L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 595<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 15_74L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 629<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 15_74L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest005.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (64916L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 149_16L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 0L<Cent>; OfWhichPrincipal = 500_00L<Cent> }))
        actual |> should equal expected

    [<Fact>]
    let ``6) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 12)
            StartDate = Date(2021, 12, 26)
            Principal = 150000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2022, 1, 7), 6, ValueNone)
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [||]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 0<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
                RoundingOptions = RoundingOptions.recommended
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
        }

        let actualPayments = [|
            { PaymentDay = 12<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 12<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 15<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 15<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 15<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 500_00L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 43<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 43<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (500_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 45<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 1540_00L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/EdgeCaseTest006.md"
                return quote.QuoteResult
            }

        let expected = ValueSome (PaymentQuote (-76_80L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 0L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 0L<Cent>; OfWhichPrincipal = -76_80L<Cent> }))
        actual |> should equal expected


    [<Fact>]
    let ``7) Quote returning nothing`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 14)
            StartDate = Date(2024, 2, 2)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2024, 2, 22), 4, ValueNone)
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [||]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 0<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
                RoundingOptions = RoundingOptions.recommended
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
        }

        let actualPayments = [|
            { PaymentDay = 6<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Failed (2_00L<Cent>, [||]); Metadata = Map.empty } }
            { PaymentDay = 16<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 97_01L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 16<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 97_01L<Cent>; Metadata = Map.empty } }
        |]

        let originalFinalPaymentDay = ((Date(2024, 5, 22) - Date(2024, 2, 2)).Days) * 1<OffsetDay>

        let (rp: RescheduleParameters) = {
            FeesSettlementRefund = Fees.SettlementRefund.ProRata (ValueSome originalFinalPaymentDay)
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay =  58<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Rescheduled 5000L<Cent>; Metadata = Map.empty } }
            |]
            RateOnNegativeBalance = ValueSome (Interest.Rate.Annual (Percent 8m))
            PromotionalInterestRates = [||]
            ChargesHolidays = [||]
            IntendedPurpose = IntendedPurpose.Settlement <| ValueSome 88<OffsetDay>
        }

        let result = reschedule sp rp actualPayments
        result |> ValueOption.iter(snd >> _.ScheduleItems >> Formatting.outputListToHtml "out/EdgeCaseTest007.md")

        let actual = result |> ValueOption.map (fun (_, s) -> s.ScheduleItems |> Array.last)

        let expected = ValueSome ({
            OffsetDate = Date(2024, 4, 30)
            OffsetDay = 88<OffsetDay>
            Advances = [||]
            ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
            Window = 2
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueSome 83_74L<Cent>
            NetEffect = 83_74L<Cent>
            PaymentStatus = Generated
            BalanceStatus = ClosedBalance
            NewInterest = 16_20.960m<Cent>
            NewCharges = [||]
            PrincipalPortion = 67_54L<Cent>
            FeesPortion = 0L<Cent>
            InterestPortion = 16_20L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = 0L<Cent>
            FeesBalance = 0L<Cent>
            InterestBalance = 0m<Cent>
            ChargesBalance = 0L<Cent>
            SettlementFigure = 83_74L<Cent>
            FeesRefundIfSettled = 0L<Cent>
        })
        actual |> should equal expected

    [<Fact>]
    let ``8) Partial write-off`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 14)
            StartDate = Date(2024, 2, 2)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2024, 2, 22), 4, ValueNone)
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [||]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 0<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
                RoundingOptions = RoundingOptions.recommended
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
        }

        let actualPayments = [|
            { PaymentDay = 6<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.WriteOff 42_00L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 16<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 97_01L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 16<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 97_01L<Cent>; Metadata = Map.empty } }
        |]

        let originalFinalPaymentDay = ((Date(2024, 5, 22) - Date(2024, 2, 2)).Days) * 1<OffsetDay>

        let (rp: RescheduleParameters) = {
            FeesSettlementRefund = Fees.SettlementRefund.ProRata (ValueSome originalFinalPaymentDay)
            PaymentSchedule = IrregularSchedule [|
                { PaymentDay =  58<OffsetDay>; PaymentDetails = ScheduledPayment { ScheduledPaymentType = ScheduledPaymentType.Rescheduled 5000L<Cent>; Metadata = Map.empty } }
            |]
            RateOnNegativeBalance = ValueSome (Interest.Rate.Annual (Percent 8m))
            PromotionalInterestRates = [||]
            ChargesHolidays = [||]
            IntendedPurpose = IntendedPurpose.Settlement <| ValueSome 88<OffsetDay>
        }

        let result = reschedule sp rp actualPayments
        result |> ValueOption.iter(snd >> _.ScheduleItems >> Formatting.outputListToHtml "out/EdgeCaseTest008.md")

        let actual = result |> ValueOption.map (fun (_, s) -> s.ScheduleItems |> Array.last)

        let expected = ValueSome ({
            OffsetDate = Date(2024, 4, 30)
            OffsetDay = 88<OffsetDay>
            Advances = [||]
            ScheduledPayment = { ScheduledPaymentType= ScheduledPaymentType.None; Metadata = Map.empty }
            Window = 2
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueSome 10_19L<Cent>
            NetEffect = 10_19L<Cent>
            PaymentStatus = Generated
            BalanceStatus = ClosedBalance
            NewInterest = 1_97.280m<Cent>
            NewCharges = [||]
            PrincipalPortion = 8_22L<Cent>
            FeesPortion = 0L<Cent>
            InterestPortion = 1_97L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = 0L<Cent>
            FeesBalance = 0L<Cent>
            InterestBalance = 0m<Cent>
            ChargesBalance = 0L<Cent>
            SettlementFigure = 10_19L<Cent>
            FeesRefundIfSettled = 0L<Cent>
        })
        actual |> should equal expected

    [<Fact>]
    let ``9) Negative principal balance accruing interest`` () =
        let sp = {
            AsOfDate = Date(2024, 4, 5)
            StartDate = Date(2023, 5, 5)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2023, 5, 10), 4, ValueNone)
            PaymentOptions = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
            }
            FeesAndCharges = {
                Fees = [||]
                FeesAmortisation = Fees.FeeAmortisation.AmortiseProportionately
                FeesSettlementRefund = Fees.SettlementRefund.ProRata ValueNone
                Charges = [||]
                ChargesHolidays = [||]
                ChargesGrouping = OneChargeTypePerDay
                LatePaymentGracePeriod = 0<DurationDay>
            }
            Interest = {
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueSome (Interest.Rate.Annual (Percent 8m))
            }
            Calculation = {
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
                RoundingOptions = RoundingOptions.recommended
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
        }

        let actualPayments = [|
            { PaymentDay = 5<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 111_00L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 21<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 181_01L<Cent>; Metadata = Map.empty } }
        |]

        let schedule =
            actualPayments
            |> Amortisation.generate sp (IntendedPurpose.Statement StatementType.InformationOnly) ScheduleType.Original false

        schedule |> ValueOption.iter(_.ScheduleItems >> Formatting.outputListToHtml "out/EdgeCaseTest009.md")

        let actual = schedule |> ValueOption.map (_.ScheduleItems >> Array.last)
        let expected = ValueSome {
            OffsetDate = Date(2023, 8, 10)
            OffsetDay = 97<OffsetDay>
            Advances = [||]
            ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.Original 87_67L<Cent>; Metadata = Map.empty }
            Window = 4
            PaymentDue = 0L<Cent>
            ActualPayments = [||]
            GeneratedPayment = ValueNone
            NetEffect = 0L<Cent>
            PaymentStatus = NoLongerRequired
            BalanceStatus = RefundDue
            NewInterest = -8.79210959m<Cent>
            NewCharges = [||]
            PrincipalPortion = 0L<Cent>
            FeesPortion = 0L<Cent>
            InterestPortion = 0L<Cent>
            ChargesPortion = 0L<Cent>
            FeesRefund = 0L<Cent>
            PrincipalBalance = -12_94L<Cent>
            FeesBalance = 0L<Cent>
            InterestBalance = -21.55484933m<Cent>
            ChargesBalance = 0L<Cent>
            SettlementFigure = 0L<Cent>
            FeesRefundIfSettled = 0L<Cent>
        }
        actual |> should equal expected
