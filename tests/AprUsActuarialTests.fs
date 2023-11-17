namespace FSharp.Finance.Tests

open System
open Xunit
open FsUnit.Xunit

open FSharp.Finance

/// https://www.consumerfinance.gov/rules-policy/regulations/1026/j/
/// 
/// Appendix J to Part 1026 — Annual Percentage Rate Computations for Closed-End Credit Transactions
///
/// (c) Examples for the actuarial method
module AprUsActuarialTests =

    open Apr
    open UnitPeriod

    /// (c)(1) Single advance transaction, with or without an odd first period, and otherwise regular
    let calculate1 advanceAmount payment paymentCount intervalSchedule advanceDate =
        let consummationDate = advanceDate
        let firstFinanceChargeEarnedDate = consummationDate
        let advances = [| { TransferType = Advance; Date = consummationDate; Amount = advanceAmount } |]
        let payments = intervalSchedule |> Schedule.generate paymentCount Schedule.Forward |> Array.map(fun dt -> { TransferType = Payment; Date = dt; Amount = payment })
        generalEquation consummationDate firstFinanceChargeEarnedDate advances payments
        |> Percent.fromDecimal |> Percent.round 2

    [<Fact>]
    let ``Example (c)(1)(i): Monthly payments (regular first period)`` () =
        let actual = calculate1 500000<Cent> 23000<Cent> 24 (Monthly (1, MonthlyConfig (1978, 2, TrackingDay 10))) (DateTime(1978, 1, 10))
        let expected = 9.69m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(1)(ii): Monthly payments (long first period)`` () =
        let actual = calculate1 600000<Cent> 20000<Cent> 36 (Monthly (1, MonthlyConfig (1978, 4, TrackingDay 1))) (DateTime(1978, 2, 10))
        let expected = 11.82m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(1)(iii): Semimonthly payments (short first period)`` () =
        let actual = calculate1 500000<Cent> 21917<Cent> 24 (SemiMonthly (SemiMonthlyConfig (1978, 3, TrackingDay 1, TrackingDay 16))) (DateTime(1978, 2, 23))
        let expected = 10.34m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(1)(iv): Quarterly payments (long first period)`` () =
        let actual = calculate1 1000000<Cent> 38500<Cent> 40 (Monthly (3, MonthlyConfig (1978, 10, TrackingDay 1))) (DateTime(1978, 5, 23))
        let expected = 8.97m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(1)(v): Weekly payments (long first period)`` () =
        let actual = calculate1 50000<Cent> 1760<Cent> 30 (Weekly (1, DateTime(1978, 4, 21))) (DateTime(1978, 3, 20))
        let expected = 14.96m<Percent>
        actual |> should equal expected

    /// (c)(2) Single advance transaction, with an odd first payment, with or without an odd first period, and otherwise regular
    let calculate2 advanceAmount firstPayment regularPaymentAmount regularPaymentCount intervalSchedule advanceDate =
        let consummationDate = advanceDate
        let firstFinanceChargeEarnedDate = consummationDate
        let advances = [| { TransferType = Advance; Date = consummationDate; Amount = advanceAmount } |]
        let payments = intervalSchedule |> Schedule.generate regularPaymentCount Schedule.Forward |> Array.map(fun dt -> { TransferType = Payment; Date = dt; Amount = regularPaymentAmount })
        let payments' = Array.concat [| [| firstPayment |]; payments |]
        generalEquation consummationDate firstFinanceChargeEarnedDate advances payments' |> fun apr -> Decimal.Round(apr, 10)
        |> Percent.fromDecimal |> Percent.round 2

    [<Fact>]
    let ``Example (c)(2)(i): Monthly payments (regular first period and irregular first payment)`` () =
        let firstPayment = { TransferType = Advance; Amount = 25000<Cent>; Date = DateTime(1978, 2, 10) } 
        let actual = calculate2 500000<Cent> firstPayment 23000<Cent> 23 (Monthly (1, MonthlyConfig (1978, 3, TrackingDay 10))) (DateTime(1978, 1, 10))
        let expected = 10.08m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(2)(ii): Payments every 4 weeks (long first period and irregular first payment)`` () =
        let firstPayment = { TransferType = Advance; Amount = 3950<Cent>; Date = DateTime(1978, 4, 20) }
        let actual = calculate2 40000<Cent> firstPayment 3831<Cent> 11 (Weekly (4, DateTime(1978, 5, 18))) (DateTime(1978, 3, 18))
        let expected = 28.50m<Percent>
        actual |> should equal expected

    /// (c)(3) Single advance transaction, with an odd final payment, with or without an odd first period, and otherwise regular
    let calculate3 advanceAmount lastPayment regularPaymentAmount regularPaymentCount intervalSchedule advanceDate =
        let consummationDate = advanceDate
        let firstFinanceChargeEarnedDate = consummationDate
        let advances = [| { TransferType = Advance; Date = consummationDate; Amount = advanceAmount } |]
        let payments = intervalSchedule |> Schedule.generate regularPaymentCount Schedule.Forward |> Array.map(fun dt -> { TransferType = Payment; Date = dt; Amount = regularPaymentAmount })
        let payments' = Array.concat [| payments; [| lastPayment |] |]
        generalEquation consummationDate firstFinanceChargeEarnedDate advances payments'
        |> Percent.fromDecimal |> Percent.round 2

    [<Fact>]
    let ``Example (c)(3)(i): Monthly payments (regular first period and irregular final payment)`` () =
        let lastPayment = { TransferType = Advance; Amount = 28000<Cent>; Date = DateTime(1978, 2, 10).AddMonths(23) }
        let actual = calculate3 500000<Cent> lastPayment 23000<Cent> 23 (Monthly (1, MonthlyConfig(1978, 2, TrackingDay 10))) (DateTime(1978, 1, 10))
        let expected = 10.50m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(3)(ii): Payments every 2 weeks (short first period and irregular final payment)`` () =
        let lastPayment = { TransferType = Advance; Amount = 3000<Cent>; Date = DateTime(1978, 4, 11).AddDays(14.*19.) }
        let actual = calculate3 20000<Cent> lastPayment 950<Cent> 19 (Weekly (2, DateTime(1978, 4, 11))) (DateTime(1978, 4, 3))
        let expected = 12.22m<Percent>
        actual |> should equal expected

     /// (c)(4) Single advance transaction, with an odd first payment, odd final payment, with or without an odd first period, and otherwise regular
    let calculate4 advanceAmount firstPayment lastPayment regularPaymentAmount regularPaymentCount intervalSchedule advanceDate =
        let consummationDate = advanceDate
        let firstFinanceChargeEarnedDate = consummationDate
        let advances = [| { TransferType = Advance; Date = consummationDate; Amount = advanceAmount } |]
        let payments = intervalSchedule |> Schedule.generate regularPaymentCount Schedule.Forward |> Array.map(fun dt -> { TransferType = Payment; Date = dt; Amount = regularPaymentAmount })
        let payments' = Array.concat [| [| firstPayment |]; payments; [| lastPayment |] |]
        generalEquation consummationDate firstFinanceChargeEarnedDate advances payments'
        |> Percent.fromDecimal |> Percent.round 2

    [<Fact>]
    let ``Example (c)(4)(i): Monthly payments (regular first period, irregular first payment, and irregular final payment)`` () =
        let firstPayment = { TransferType = Payment; Amount = 25000<Cent>; Date = DateTime(1978, 2, 10) }
        let lastPayment = { TransferType = Payment; Amount = 28000<Cent>; Date = DateTime(1978, 3, 10).AddMonths(22) }
        let actual = calculate4 500000<Cent> firstPayment lastPayment 23000<Cent> 22 (Monthly (1, MonthlyConfig(1978, 3, TrackingDay 10))) (DateTime(1978, 1, 10))
        let expected = 10.90m<Percent>
        actual |> should equal expected

    [<Fact>]
    let ``Example (c)(4)(ii): Payments every two months (short first period, irregular first payment, and irregular final payment)`` () =
        let firstPayment = { TransferType = Payment; Amount = 44936<Cent>; Date = DateTime(1978, 3, 1) }
        let lastPayment = { TransferType = Payment; Amount = 20000<Cent>; Date = DateTime(1978, 5, 1).AddMonths(36) }
        let actual = calculate4 800000<Cent> firstPayment lastPayment 46500<Cent> 18 (Monthly (2, MonthlyConfig(1978, 5, TrackingDay 1))) (DateTime(1978, 1, 10))
        let expected = 7.30m<Percent>
        actual |> should equal expected

    /// (c)(5) Single advance, single payment transaction
    let calculate5 advance payment =
        let consummationDate = advance.Date
        let firstFinanceChargeEarnedDate = consummationDate
        let advances = [| advance |]
        let payments = [| payment |]
        generalEquation consummationDate firstFinanceChargeEarnedDate advances payments
        |> Percent.fromDecimal |> Percent.round 2

    [<Fact>]
    let ``Example (c)(5)(i): Single advance, single payment (term of less than 1 year, measured in days)`` () =
        let advance = { TransferType = Payment; Date = DateTime(1978, 1, 3); Amount = 100000<Cent> }
        let payment = { TransferType = Payment; Date = DateTime(1978, 9, 15); Amount = 108000<Cent> }
        let actual = calculate5 advance payment
        let expected = 11.45m<Percent>
        actual |> should equal expected

    /// examples created while debugging the notebook, used to test edge-cases, all confirmed using Excel
    module ExtraExamples =

        [<Fact>]
        let ``Example (c)(1)(iv) [modified]: Quarterly payments (shorter first period)`` () =
            let actual = calculate1 1000000<Cent> 38500<Cent> 40 (Monthly (3, MonthlyConfig (1978, 10, TrackingDay 1))) (DateTime(1978, 6, 23))
            let expected = 9.15m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Example (c)(1)(iv) [modified]: Quarterly payments (shorter first period: less than unit-period)`` () =
            let actual = calculate1 1000000<Cent> 38500<Cent> 40 (Monthly (3, MonthlyConfig (1978, 10, TrackingDay 1))) (DateTime(1978, 7, 23))
            let expected = 9.32m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Daily payments`` () =
            let actual = calculate1 100000<Cent> 22000<Cent> 5 (Daily (DateTime(2023,11,30))) (DateTime(2023,10,26))
            let expected = 94.15m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Weekly payments with long first period`` () =
            let actual = calculate1 100000<Cent> 25000<Cent> 5 (Weekly(1, DateTime(2023,11,30))) (DateTime(2023,10,28))
            let expected = 176.52m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Weekly payments with first period equal to unit-period`` () =
            let actual = calculate1 100000<Cent> 25000<Cent> 5 (Weekly(1, DateTime(2023,11,30))) (DateTime(2023,11,23))
            let expected = 412.40m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Weekly payments with first period shorter than unit-period`` () =
            let actual = calculate1 100000<Cent> 25000<Cent> 5 (Weekly(1, DateTime(2023,11,30))) (DateTime(2023,11,24))
            let expected = 434.30m<Percent>
            actual |> should equal expected

        [<Fact>]
        let ``Yearly payments`` () =
            let actual = calculate1 100000<Cent> 50000<Cent> 5 (Monthly (12, MonthlyConfig (2023, 11, TrackingDay 30))) (DateTime(2023, 10, 26))
            let expected = 78.34m<Percent>
            actual |> should equal expected
