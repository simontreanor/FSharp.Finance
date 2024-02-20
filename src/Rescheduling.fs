namespace FSharp.Finance.Personal

open System
open CustomerPayments

/// functions for rescheduling payments after an original schedule failed to amortise
module Rescheduling =

    /// whether a payment plan is generated according to a regular schedule or is an irregular array of payments
    [<Struct>]
    type PaymentPlanSchedule =
        /// a regular plan starting on a particular date with a certain interval and a fixed amount
        | RegularPlan of PaymentPlanStart: DateTime * UnitPeriod: UnitPeriod.UnitPeriod * Amount: int64<Cent>
        /// just a bunch of payments
        | IrregularPlan of CustomerPayment array

    /// the parameters used for setting up a payment plan
    [<Struct>]
    type PaymentPlanParameters = {
        /// whether the plan is autogenerated or a manual plan provided
        Schedule: PaymentPlanSchedule
        /// any interest rate applicable to the payment plan
        InterestRate: Interest.Rate voption
        /// any grace-period after which any late-payment charges are applicable
        LatePaymentGracePeriod: int<DurationDay> voption
        /// the configuration of any late-payment charge
        LatePaymentCharge: Amount voption
    }

    /// creates a payment plan for fully amortising an outstanding balance based on a payment amount, unit-period config and interest rate
    let createPaymentPlan (payment: int64<Cent>) unitPeriodConfig interestRate (outstandingBalance: int64<Cent>) originalStartDate =
        let count = // to-do: add a function that takes this count as a parameter
            let roughUnitPeriodLength = unitPeriodConfig |> UnitPeriod.Config.roughLength
            let initialCount = decimal outstandingBalance / decimal payment |> Math.Ceiling
            let estimatedYears = (roughUnitPeriodLength * initialCount) / 365m
            let annualInterestRate = interestRate |> Interest.Rate.annual |> Percent.toDecimal
            (1m + (annualInterestRate * estimatedYears)) * initialCount |> Math.Ceiling |> int
        unitPeriodConfig
        |> UnitPeriod.generatePaymentSchedule count UnitPeriod.Direction.Forward
        |> Array.map(fun d -> { PaymentDay = d |> OffsetDay.fromDate originalStartDate ; PaymentDetails = ScheduledPayment payment })

    /// to-do: create a function to disapply all interest paid so far
    let disapplyInterest =
        ()