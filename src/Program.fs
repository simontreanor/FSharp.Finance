open System
open FSharp.Finance

open Amortisation

let amortisationScheduleInfo =
    let principal = 1200m
    let productFees = Percentage (1.8947m, ValueNone)
    let annualInterestRate = 0.0995m
    let startDate = DateTime.Today
    let unitPeriodConfig = UnitPeriod.Weekly(2, DateTime.Today.AddDays(15.))
    let paymentCount = 11 // to-do: restore function to determine this based on max loan length?
    { Principal = principal; ProductFees = productFees; AnnualInterestRate = annualInterestRate; StartDate = startDate; UnitPeriodConfig = unitPeriodConfig; PaymentCount = paymentCount }
    |> createRegularScheduleInfo UnitPeriodsOnly

amortisationScheduleInfo.Schedule.Items
|> Formatting.outputListToHtml "Output.md" (ValueSome 180)

exit 0

// to-do: investigate the effect of rounding on the results - also the last payment should be less than the level payments
