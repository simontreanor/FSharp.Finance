open System
open FSharp.Finance.Personal

open RegularPayment

let startDate = DateTime.Today.AddDays(-421.)

let sp = {
    StartDate = startDate
    Principal = 1200 * 100<Cent>
    ProductFees = ValueSome <| Percentage (189.47m<Percent>, ValueNone)
    InterestRate = AnnualInterestRate 9.95m<Percent>
    InterestCap = ValueNone
    UnitPeriodConfig = UnitPeriod.Weekly(2, startDate.AddDays(15.))
    PaymentCount = 11
}

calculateSchedule sp
|> _.Items
|> Formatting.outputListToHtml "RegularPayment.md" (ValueSome 180)

open IrregularPayment

let actualPayments =
    [| 180 .. 7 .. 2098 |]
    |> Array.map(fun i ->
        { Day = i * 1<Day>; Adjustments = [| ActualPayment 2500<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
    )

// let actualPayments = [|
//     { Day = 15<Day>; Adjustments = [| ActualPayment 32315<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
//     { Day = 18<Day>; Adjustments = [| ActualPayment 10000<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
//     { Day = 19<Day>; Adjustments = [| ActualPayment -10000<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
// |]

// let actualPayments = [|
//     { Day = 170<Day>; Adjustments = [| ActualPayment 300<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
//     { Day = 177<Day>; Adjustments = [| ActualPayment 300<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
//     { Day = 184<Day>; Adjustments = [| ActualPayment 300<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
//     { Day = 191<Day>; Adjustments = [| ActualPayment 300<Cent> |]; PaymentStatus = ValueNone; PenaltyCharges = [||] }
// |]

actualPayments
|> applyPayments sp
|> Formatting.outputListToHtml "IrregularPayment.md" (ValueSome 300)

exit 0
