namespace FSharp.Finance.Personal.Tests

open Xunit
open FsUnit.Xunit

open FSharp.Finance.Personal

module SettlementTests =

    open Amortisation
    open Calculation
    open Currency
    open DateDay
    open Formatting
    open PaymentSchedule
    open Percentages
    open Quotes
    open ValueOptionCE

    let interestCapExample : Interest.Cap = {
        TotalAmount = ValueSome (Amount.Percentage (Percent 100m, ValueNone, ValueSome RoundDown))
        DailyAmount = ValueSome (Amount.Percentage (Percent 0.8m, ValueNone, ValueNone))
    }

    [<Fact>]
    let ``1) Final payment due on Friday: what would I pay if I paid it today?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 19)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            ScheduleConfig = AutoGenerateSchedule { UnitPeriodConfig = UnitPeriod.Config.Monthly(1, 2023, 12, 22); PaymentCount = 4; MaxDuration = ValueNone }
            PaymentConfig = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
                PaymentRounding = RoundUp
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
            FeeConfig = Fee.Config.DefaultValue
            ChargeConfig = Charge.Config.DefaultValue
            InterestConfig = {
                Method = Interest.Method.Simple
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
                InterestRounding = RoundDown
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
            }
        }

        let actualPayments =
            Map [
                24<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                55<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                86<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
            ]

        let actual =
            let quote = getQuote IntendedPurpose.SettlementOnAsOfDay sp actualPayments
            quote.RevisedSchedule.ScheduleItems |> outputMapToHtml "out/SettlementTest001.md" false
            let scheduledItem = quote.RevisedSchedule.ScheduleItems |> Map.find 112<OffsetDay>
            quote.QuoteResult, scheduledItem

        let expected =
            PaymentQuote {
                PaymentValue = 98_52L<Cent>
                OfWhichPrincipal = 81_56L<Cent>
                OfWhichFees = 0L<Cent>
                OfWhichInterest = 16_96L<Cent>
                OfWhichCharges = 0L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            },
            {
                OffsetDate = Date(2024, 3, 19)
                Advances = [||]
                ScheduledPayment = ScheduledPayment.DefaultValue
                Window = 3
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = GeneratedValue 98_52L<Cent>
                NetEffect = 98_52L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                OriginalSimpleInterest = 0L<Cent>
                ContractualInterest = 0m<Cent>
                SimpleInterest = 16_96.448m<Cent>
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

        actual |> should equal expected

    [<Fact>]
    let ``2) Final payment due on Friday: what would I pay if I paid it one week too late?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 29)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            ScheduleConfig = AutoGenerateSchedule { UnitPeriodConfig = UnitPeriod.Config.Monthly(1, 2023, 12, 22); PaymentCount = 4; MaxDuration = ValueNone }
            PaymentConfig = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
                PaymentRounding = RoundUp
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
            FeeConfig = Fee.Config.DefaultValue
            ChargeConfig = Charge.Config.DefaultValue
            InterestConfig = {
                Method = Interest.Method.Simple
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
                InterestRounding = RoundDown
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
            }
        }

        let actualPayments =
            Map [
                24<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                55<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                86<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
            ]

        let actual =
            let quote = getQuote IntendedPurpose.SettlementOnAsOfDay sp actualPayments
            quote.RevisedSchedule.ScheduleItems |> outputMapToHtml "out/SettlementTest002.md" false
            let scheduledItem = quote.RevisedSchedule.ScheduleItems |> Map.find 122<OffsetDay>
            quote.QuoteResult, scheduledItem

        let expected =
            PaymentQuote {
                PaymentValue = 105_04L<Cent>
                OfWhichPrincipal = 81_56L<Cent>
                OfWhichFees = 0L<Cent>
                OfWhichInterest = 23_48L<Cent>
                OfWhichCharges = 0L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            },
            {
                OffsetDate = Date(2024, 3, 29)
                Advances = [||]
                ScheduledPayment = ScheduledPayment.DefaultValue
                Window = 4
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = GeneratedValue 105_04L<Cent>
                NetEffect = 105_04L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                OriginalSimpleInterest = 0L<Cent>
                ContractualInterest = 0m<Cent>
                SimpleInterest = 4_56.736m<Cent>
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

        actual |> should equal expected

    [<Fact>]
    let ``3) Final payment due on Friday: what if I pay £50 on Friday and the rest next week one week too late?`` () =
        let sp = {
            AsOfDate = Date(2024, 3, 29)
            StartDate = Date(2023, 11, 28)
            Principal = 25000L<Cent>
            ScheduleConfig = AutoGenerateSchedule { UnitPeriodConfig = UnitPeriod.Config.Monthly(1, 2023, 12, 22); PaymentCount = 4; MaxDuration = ValueNone }
            PaymentConfig = {
                ScheduledPaymentOption = AsScheduled
                CloseBalanceOption = LeaveOpenBalance
                PaymentRounding = RoundUp
                PaymentTimeout = 0<DurationDay>
                MinimumPayment = NoMinimumPayment
            }
            FeeConfig = Fee.Config.DefaultValue
            ChargeConfig = Charge.Config.DefaultValue
            InterestConfig = {
                Method = Interest.Method.Simple
                StandardRate = Interest.Rate.Daily (Percent 0.8m)
                Cap = interestCapExample
                InitialGracePeriod = 0<DurationDay>
                PromotionalRates = [||]
                RateOnNegativeBalance = ValueNone
                InterestRounding = RoundDown
                AprMethod = Apr.CalculationMethod.UnitedKingdom(3)
            }
        }

        let actualPayments =
            Map [
                24<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                55<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                86<OffsetDay>, [| ActualPayment.QuickConfirmed 100_53L<Cent> |]
                115<OffsetDay>, [| ActualPayment.QuickConfirmed 50_00L<Cent> |]
            ]

        let actual =
            let quote = getQuote IntendedPurpose.SettlementOnAsOfDay sp actualPayments
            quote.RevisedSchedule.ScheduleItems |> outputMapToHtml "out/SettlementTest003.md" false
            let scheduledItem = quote.RevisedSchedule.ScheduleItems |> Map.find 122<OffsetDay>
            quote.QuoteResult, scheduledItem

        let expected =
            PaymentQuote {
                PaymentValue = 53_30L<Cent>
                OfWhichPrincipal = 50_48L<Cent>
                OfWhichFees = 0L<Cent>
                OfWhichInterest = 2_82L<Cent>
                OfWhichCharges = 0L<Cent>
                FeesRefundIfSettled = 0L<Cent>
            },
            {
                OffsetDate = Date(2024, 3, 29)
                Advances = [||]
                ScheduledPayment = ScheduledPayment.DefaultValue
                Window = 4
                PaymentDue = 0L<Cent>
                ActualPayments = [||]
                GeneratedPayment = GeneratedValue 53_30L<Cent>
                NetEffect = 53_30L<Cent>
                PaymentStatus = Generated
                BalanceStatus = ClosedBalance
                OriginalSimpleInterest = 0L<Cent>
                ContractualInterest = 0m<Cent>
                SimpleInterest = 2_82.688m<Cent>
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

        actual |> should equal expected
