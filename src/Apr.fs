namespace FSharp.Finance.Personal

open System
open Util

module Apr =

    /// the calculation method used to determine the APR
    [<RequireQualifiedAccess>]
    [<Struct>]
    type CalculationMethod =
        | UnitedKingdom
        | UsActuarial
        | UnitedStatesRule

    /// basic calculation to determine the APR
    let annualPercentageRate unitPeriodRate unitPeriodsPerYear =
        unitPeriodRate * unitPeriodsPerYear

    /// whether a transfer is an advance or a payment
    [<Struct>]
    type TransferType =
        | Advance
        | Payment

    /// details of an advance or a payment
    [<Struct>]
    type Transfer = {
        TransferType: TransferType
        Date: DateTime
        Amount: int64<Cent>
    }

    /// APR as in https://www.handbook.fca.org.uk/handbook/MCOB/10/?view=chapter
    module UnitedKingdom =
        [<Struct>]
        type AprResult =
        | More
        | Less
        | Accurate


        /// Payments: duedate * repayment sum
        let ``APR Calculation`` (startDay:DateTime) (principalValue) (payments:(DateTime*decimal)[]) =

            let S = principalValue

            let paymentsMap =
                payments |> Array.map( fun(day, singlePayment) ->
                    let tK = (decimal (day.Date-startDay.Date).Days)/365m
                    let Ak = singlePayment
                    tK, Ak)

            let ``APR formula`` i =
                let res =
                    let max = float Decimal.MaxValue
                    paymentsMap |> Array.sumBy( fun(tK, Ak) ->
                        let d = Math.Pow((1. + i), (float tK))
                        if d <= max then Ak/(decimal d)
                        else 0m
                    )
                let tolerance = 0.00001m
                if res < S - tolerance then AprResult.Less
                elif res > S + tolerance then AprResult.More
                else AprResult.Accurate

            match ``APR formula`` 0. with
            | AprResult.Accurate -> 0m
            | _ ->
                let rec ``fit APR`` min max iter =
                    let middle =
                        let x = (max-min) / 2.
                        if x = max then max * 2.
                        elif x = min then
                            if min = 0. then -1000. else min / 2.
                        else x
                    if iter> 1000 then 100.*(min+middle)
                    else
                    match ``APR formula`` (min+middle) with
                    | More -> ``fit APR`` (min+middle) max (iter+1)
                    | Less -> ``fit APR`` min (max-middle) (iter+1)
                    | Accurate -> 100.*(min+middle)
                decimal(``fit APR`` 0. 10000. 0)

        let calculateApr (startDate: DateTime) (principal: int64<Cent>) (transfers: Transfer array) =
            if principal = 0L<Cent> || Array.isEmpty transfers then ValueNone else
            let ak = [| principal, 0m |]
            let a'k' =
                transfers
                |> Array.filter(fun t -> t.TransferType = Payment)
                |> Array.map(fun t ->
                    t.Amount, decimal (t.Date.Date - startDate.Date).Days / 365m
                )
            4.2m
            |> Array.solve(fun i ->
                if i < 0m then ValueNone else
                let aa = ak |> Array.sumBy(fun (a, t) -> Cent.toDecimal a / ((1m + i) |> powm t))
                let pp = a'k' |> Array.sumBy(fun (a', t') -> Cent.toDecimal a' / ((1m + i) |> powm t'))
                if Decimal.Round(pp - aa, 6) = 0m then
                    ValueNone
                else
                    ValueSome (i, i * ((pp / aa) |> powi 4))
            ) (if Array.length transfers < 4 then 1000 else 100)
            |> ValueOption.map Percent.fromDecimal

    /// APR as in https://www.consumerfinance.gov/rules-policy/regulations/1026/j/
    module UsActuarial =

        /// (b)(5)(i) The number of days between 2 dates shall be the number of 24-hour intervals between any point in time on the first 
        /// date to the same point in time on the second date.
        let daysBetween (date1: DateTime) (date2: DateTime) =
            (date2.Date - date1.Date).Days

        /// (b)(5)(iv) If the unit-period is a day, [...] the number of full unit-periods and the remaining fractions of a unit-period 
        /// shall be determined by dividing the number of days between the 2 given dates by the number of days per unit-period. If the 
        /// unit-period is a day, the number of unit-periods per year shall be 365. [...]
        let dailyUnitPeriods termStart transfers =
            let transferDates = transfers |> Array.map _.Date
            let offset = daysBetween termStart (transferDates |> Array.head)
            transfers
            |> Array.mapi(fun i t -> t, { Quotient = i + offset; Remainder = 0m })

        /// (b)(5)(ii) If the unit-period is a month, the number of full unit-periods between 2 dates shall be the number of months 
        /// measured back from the later date. The remaining fraction of a unit-period shall be the number of days measured forward from 
        /// the earlier date to the beginning of the first full unit-period, divided by 30. If the unit-period is a month, there are 
        /// 12 unit-periods per year.
        ///
        /// (b)(5)(iii) If the unit-period is [...] a multiple of a month not exceeding 11 months, the number of days between 2 dates 
        /// shall be 30 times the number of full months measured back from the later date, plus the number of remaining days. The number 
        /// of full unit-periods and the remaining fraction of a unit-period shall be determined by dividing such number of days [...] by 
        /// the appropriate multiple of 30 in the case of a multimonthly unit-period. [...] If the number of unit-periods is a multiple 
        /// of a month, the number of unit-periods per year shall be 12 divided by the number of months per unit-period.
        /// 
        /// (b)(5)(v) If the unit-period is a year, the number of full unit-periods between 2 dates shall be the number of full years (each 
        /// equal to 12 months) measured back from the later date. The remaining fraction of a unit-period shall be
        ///
        /// > (A) The remaining number of months divided by 12 if the remaining interval is equal to a whole number of months, or
        ///
        /// > (B) The remaining number of days divided by 365 if the remaining interval is not equal to a whole number of months.
        let monthlyUnitPeriods (multiple: int) termStart transfers =
            let transferDates = transfers |> Array.map _.Date
            let transferCount = transfers |> Array.length
            let frequency = transferDates |> Schedule.detect Schedule.Reverse (UnitPeriod.Month 1)
            let schedule = Schedule.generate ((transferCount + 1) * multiple) Schedule.Reverse frequency |> Array.filter(fun dt -> dt >= termStart)
            let scheduleCount = schedule |> Array.length
            let lastWholeMonthBackIndex = 0
            let lastWholeUnitPeriodBackIndex = (scheduleCount - 1) % multiple
            let offset = (scheduleCount - 1) - ((transferCount - 1) * multiple) |> fun i -> Decimal.Floor(decimal i / decimal multiple) |> int
            [| 0 .. (transferCount - 1) |]
            |> Array.map(fun i ->
                let wholeUnitPeriods = i + offset
                let remainingMonthDays = decimal (lastWholeUnitPeriodBackIndex - lastWholeMonthBackIndex) * 30m
                let remainingDays = decimal (schedule[lastWholeMonthBackIndex] - termStart).Days
                let remainder =
                    if multiple < 12 then
                        (remainingMonthDays + remainingDays) / (30m * decimal multiple)
                    elif remainingDays = 0m then
                        decimal (lastWholeUnitPeriodBackIndex - lastWholeMonthBackIndex) / 12m
                    else
                        decimal (schedule[lastWholeUnitPeriodBackIndex] - termStart).Days / 365m
                transfers[i], { Quotient = wholeUnitPeriods; Remainder = remainder }
            )

        /// (b)(5)(iii) If the unit-period is a semimonth [...], the number of days between 2 dates shall be 30 times the number of full 
        /// months measured back from the later date, plus the number of remaining days. The number of full unit-periods and the remaining 
        /// fraction of a unit-period shall be determined by dividing such number of days by 15 in the case of a semimonthly unit-period 
        /// [...]. If the unit-period is a semimonth, the number of unit-periods per year shall be 24. [...]
        let semiMonthlyUnitPeriods termStart transfers =
            let transferDates = transfers |> Array.map _.Date
            let transferCount = transfers |> Array.length
            let frequency = transferDates |> Schedule.detect Schedule.Reverse UnitPeriod.SemiMonth
            let schedule = Schedule.generate (transferCount + 2) Schedule.Reverse frequency |> Array.filter(fun dt -> dt >= termStart)
            let scheduleCount = schedule |> Array.length
            let offset = scheduleCount - transferCount
            [| 0 .. (transferCount - 1) |]
            |> Array.map(fun i ->
                let lastWholeMonthBackIndex = (i + offset) % 2
                let wholeUnitPeriods = i + offset - lastWholeMonthBackIndex
                let fractional = decimal (schedule[lastWholeMonthBackIndex] - termStart).Days / 15m |> fun d -> divRem d 1m
                transfers[i], { Quotient = wholeUnitPeriods + fractional.Quotient; Remainder = fractional.Remainder }
            )

        /// (b)(5)(iv) If the unit-period is [...] a week, or a multiple of a week, the number of full unit-periods and the remaining 
        /// fractions of a unit-period shall be determined by dividing the number of days between the 2 given dates by the number of days 
        /// per unit-period. [...] If the unit-period is a week or a multiple of a week, the number of unit-periods per year shall be 
        /// 52 divided by the number of weeks per unit-period.
        let weeklyUnitPeriods (multiple: int) termStart transfers =
            let transferDates = transfers |> Array.map _.Date
            let dr  = decimal (daysBetween termStart (transferDates |> Array.head)) / (7m * decimal multiple) |> fun d -> divRem d 1m
            transfers
            |> Array.mapi(fun i t ->
                t, { Quotient = dr.Quotient + i; Remainder = dr.Remainder }
            )

        /// (b)(5)(vi) In a single advance, single payment transaction in which the term is less than a year and is equal 
        /// to a whole number of months, the number of unit-periods in the term shall be 1, and the number of 
        /// unit-periods per year shall be 12 divided by the number of months in the term or 365 divided by the 
        /// number of days in the term.
        ///
        /// (b)(5)(vii) In a single advance, single payment transaction in which the term is less than a year and is not 
        /// equal to a whole number of months, the number of unit-periods in the term shall be 1, and the number of 
        /// unit-periods per year shall be 365 divided by the number of days in the term.
        let singleUnitPeriod _ transfers =
            let transfer = transfers |> Array.exactlyOne
            [| transfer, { Quotient = 1; Remainder = 0m } |]

        /// map an array of transfers to an array of whole and fractional unit periods
        let mapUnitPeriods unitPeriod =
            match unitPeriod with
            | (UnitPeriod.NoInterval _) -> singleUnitPeriod
            | UnitPeriod.Day -> dailyUnitPeriods
            | (UnitPeriod.Week multiple) -> weeklyUnitPeriods multiple
            | UnitPeriod.SemiMonth -> semiMonthlyUnitPeriods
            | (UnitPeriod.Month multiple) -> monthlyUnitPeriods multiple

        /// (b)(8) General equation.
        let generalEquation consummationDate firstFinanceChargeEarnedDate advances payments =
            if Array.isEmpty advances || Array.isEmpty payments then 0m else
            let advanceDates = advances |> Array.map _.Date
            let paymentDates = payments |> Array.map _.Date
            let term = UnitPeriod.transactionTerm consummationDate firstFinanceChargeEarnedDate (paymentDates |> Array.last) (advanceDates |> Array.last)
            let unitPeriod = UnitPeriod.nearest term advanceDates paymentDates
            let unitPeriodsPerYear = UnitPeriod.numberPerYear unitPeriod
            let roughUnitPeriodRate =
                let paymentAverage = payments |> Array.averageBy (_.Amount >> Cent.toDecimal)
                let advanceTotal = advances |> Array.sumBy (_.Amount >> Cent.toDecimal)
                let paymentCount = payments |> Array.length |> decimal
                if advanceTotal = 0m || unitPeriodsPerYear = 0m then
                    0m
                else
                    ((paymentAverage / advanceTotal) * (paymentCount / 12m)) / unitPeriodsPerYear
            let unitPeriodRate =
                let eq = [| advances[0].Amount, 0m, 0 |]
                    // mapUnitPeriods unitPeriod term.Start advances
                    // |> Array.map(fun (tr, dr) ->
                    //     let e = dr.Remainder //The fraction of a unit-period in the time interval from the beginning of the term of the transaction to the kth advance.
                    //     let q = dr.Quotient //The number of full unit-periods from the beginning of the term of the transaction to the kth advance.
                    //     tr.Amount, e, q
                    // )
                let ft =
                    mapUnitPeriods unitPeriod term.Start payments
                    |> Array.map(fun (tr, dr) ->
                        let f = dr.Remainder //The fraction of a unit-period in the time interval from the beginning of the term of the transaction to the jth payment.
                        let t = dr.Quotient //The number of full unit-periods from the beginning of the term of the transaction to the jth payment.
                        tr.Amount, f, t
                    )
                roughUnitPeriodRate
                |> Array.solve(fun i -> //The percentage rate of finance charge per unit-period, expressed as a decimal equivalent.
                    let aa = eq |> Array.sumBy(fun (a, e, q) -> Cent.toDecimal a / ((1m + (e * i)) * ((1m + i) |> powi q)))
                    let pp = ft |> Array.sumBy(fun (p, f, t) -> Cent.toDecimal p / ((1m + (f * i)) * ((1m + i) |> powi t)))
                    if aa = 0m || Decimal.Round(pp - aa, 10) = 0m then
                        ValueNone
                    else
                        ValueSome (i, i * ((pp / aa) |> powi 2))
                ) 100
                |> ValueOption.defaultValue 0m
            annualPercentageRate unitPeriodRate unitPeriodsPerYear

    /// calculates the APR to a given precision for a single-advance transaction where the consummation date, first finance-charge earned date and
    /// advance date are all the same
    let calculate method (precision: int) advanceAmount advanceDate payments =
        match method with
        | CalculationMethod.UnitedKingdom ->
            Percent 0m
        | CalculationMethod.UsActuarial ->
            let advances = [| { TransferType = Advance; Date = advanceDate; Amount = advanceAmount } |]
            if Array.isEmpty payments then [| { TransferType = Payment; Date = advanceDate.AddYears 1; Amount = 0L<Cent> } |]
            else payments
            |> UsActuarial.generalEquation advanceDate advanceDate advances
            |> fun apr -> Decimal.Round(apr, precision) |> Percent.fromDecimal
        | CalculationMethod.UnitedStatesRule ->
            failwith "Not yet implemented"
