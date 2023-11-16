open System
open FSharp.Finance

open Amortisation

let amortisationScheduleInfo =
    let principal = 1200 * 100<Cent>
    let productFees = Percentage (189.47m<Percent>, ValueNone)
    let annualInterestRate = 9.95m<Percent>
    let startDate = DateTime.Today
    let unitPeriodConfig = UnitPeriod.Weekly(2, DateTime.Today.AddDays(15.))
    let paymentCount = 11 // to-do: restore function to determine this based on max loan length?
    { Principal = principal; ProductFees = productFees; AnnualInterestRate = annualInterestRate; StartDate = startDate; UnitPeriodConfig = unitPeriodConfig; PaymentCount = paymentCount }
    |> createRegularScheduleInfo UnitPeriodsOnly

amortisationScheduleInfo.Schedule.Items
|> Formatting.outputListToHtml "Output.md" (ValueSome 180)

exit 0
