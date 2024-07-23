namespace FSharp.Finance.Personal.Tests

open Xunit
open FsUnit.Xunit

open FSharp.Finance.Personal

module SettlementTests =

    open ArrayExtension
    open Amortisation
    open Calculation
    open Currency
    open CustomerPayments
    open DateDay
    open FeesAndCharges
    open PaymentSchedule
    open Percentages
    open Quotes
    open ValueOptionCE

    let interestCapExample : Interest.Cap = {
        Total = ValueSome (Amount.Percentage (Percent 100m, ValueNone, ValueSome RoundDown))
        Daily = ValueSome (Amount.Percentage (Percent 0.8m, ValueNone, ValueNone))
    }

    [<Fact>]
    let ``1) Final payment due on Friday: what would I pay if I paid it today?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 19)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2023, 12, 22), 4, ValueNone)
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
            { PaymentDay = 24<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 55<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 86<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/SettlementTest001.md"
                let! scheduledItem = Array.vTryLastBut 1 quote.AmendedSchedule.ScheduleItems
                return quote.QuoteResult, scheduledItem
            }

        let expected = ValueSome (
            PaymentQuote (98_52L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 16_96L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 0L<Cent>; OfWhichPrincipal = 81_56L<Cent> }),
            {
                OffsetDate = Date(2024, 3, 19)
                OffsetDay = 112<OffsetDay>
                Advances = [||]
                ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
                Window = 3
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = ValueSome 98_52L<Cent>
                NetEffect = 98_52L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                NewInterest = 16_96.448m<Cent>
                NewCharges = [||]
                PrincipalPortion = 81_56L<Cent>
                FeesPortion = 0L<Cent>
                InterestPortion = 16_96L<Cent>
                ChargesPortion = 0L<Cent>
                FeesRefund = 0L<Cent>
                PrincipalBalance = 0L<Cent>
                FeesBalance = 0L<Cent>
                InterestBalance = 0m<Cent>
                ChargesBalance = 0L<Cent>
                SettlementFigure = 98_52L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            }
        )

        actual |> should equal expected

    [<Fact>]
    let ``2) Final payment due on Friday: what would I pay if I paid it one week too late?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 29)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2023, 12, 22), 4, ValueNone)
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
            { PaymentDay = 24<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 55<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 86<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/SettlementTest002.md"
                let scheduledItem = Array.last quote.AmendedSchedule.ScheduleItems
                return quote.QuoteResult, scheduledItem
            }

        let expected = ValueSome (
            PaymentQuote (105_04L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 23_48L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 0L<Cent>; OfWhichPrincipal = 81_56L<Cent> }),
            {
                OffsetDate = Date(2024, 3, 29)
                OffsetDay = 122<OffsetDay>
                Advances = [||]
                ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
                Window = 4
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = ValueSome 105_04L<Cent>
                NetEffect = 105_04L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                NewInterest = 4_56.736m<Cent>
                NewCharges = [||]
                PrincipalPortion = 81_56L<Cent>
                FeesPortion = 0L<Cent>
                InterestPortion = 23_48L<Cent>
                ChargesPortion = 0L<Cent>
                FeesRefund = 0L<Cent>
                PrincipalBalance = 0L<Cent>
                FeesBalance = 0L<Cent>
                InterestBalance = 0m<Cent>
                ChargesBalance = 0L<Cent>
                SettlementFigure = 105_04L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            }
        )

        actual |> should equal expected

    [<Fact>]
    let ``3) Final payment due on Friday: what if I pay £50 on Friday and the rest next week one week too late?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 29)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            PaymentSchedule = RegularSchedule(UnitPeriod.Config.Monthly(1, 2023, 12, 22), 4, ValueNone)
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
            { PaymentDay = 24<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 55<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 86<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 100_53L<Cent>; Metadata = Map.empty } }
            { PaymentDay = 115<OffsetDay>; PaymentDetails = ActualPayment { ActualPaymentStatus = ActualPaymentStatus.Confirmed 50_00L<Cent>; Metadata = Map.empty } }
        |]

        let actual =
            voption {
                let! quote = getQuote (IntendedPurpose.Settlement ValueNone) sp actualPayments
                quote.AmendedSchedule.ScheduleItems |> Formatting.outputListToHtml "out/SettlementTest003.md"
                let scheduledItem = Array.last quote.AmendedSchedule.ScheduleItems
                return quote.QuoteResult, scheduledItem
            }

        let expected = ValueSome (
            PaymentQuote (53_30L<Cent>, { OfWhichCharges = 0L<Cent>; OfWhichInterest = 2_82L<Cent>; FeesRefund = 0L<Cent>; OfWhichFees = 0L<Cent>; OfWhichPrincipal = 50_48L<Cent> }),
            {
                OffsetDate = Date(2024, 3, 29)
                OffsetDay = 122<OffsetDay>
                Advances = [||]
                ScheduledPayment = { ScheduledPaymentType = ScheduledPaymentType.None; Metadata = Map.empty }
                Window = 4
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = ValueSome 53_30L<Cent>
                NetEffect = 53_30L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                NewInterest = 2_82.688m<Cent>
                NewCharges = [||]
                PrincipalPortion = 50_48L<Cent>
                FeesPortion = 0L<Cent>
                InterestPortion = 2_82L<Cent>
                ChargesPortion = 0L<Cent>
                FeesRefund = 0L<Cent>
                PrincipalBalance = 0L<Cent>
                FeesBalance = 0L<Cent>
                InterestBalance = 0m<Cent>
                ChargesBalance = 0L<Cent>
                SettlementFigure = 53_30L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            }
        )

        actual |> should equal expected

