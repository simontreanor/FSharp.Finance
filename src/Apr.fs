namespace FSharp.Finance

open System

module Apr =

    let annualPercentageRate unitPeriodRate unitPeriodsPerYear =
        unitPeriodRate * unitPeriodsPerYear

    type TransferType =
        | Advance
        | Payment

    /// an advance or a payment
    type Transfer = {
        TransferType: TransferType
        Date: DateTime
        Amount: decimal
    }

    /// (b)(5)(i) The number of days between 2 dates shall be the number of 24-hour intervals between any point in time on the first 
    /// date to the same point in time on the second date.
    let daysBetween (date1: DateTime) (date2: DateTime) =
        (date2.Date - date1.Date).TotalDays |> decimal

    /// (b)(5)(iv) If the unit-period is a day, [...] the number of full unit-periods and the remaining fractions of a unit-period 
    /// shall be determined by dividing the number of days between the 2 given dates by the number of days per unit-period. If the 
    /// unit-period is a day, the number of unit-periods per year shall be 365. [...]
    let dailyUnitPeriods termStart transfers =
        let transferDates = transfers |> Array.map(fun t -> t.Date)
        let offset = daysBetween termStart (transferDates |> Array.head)
        transfers
        |> Array.mapi(fun i t -> t, { Quotient = decimal i + offset; Remainder = 0m })

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
        let transferDates = transfers |> Array.map(fun t -> t.Date)
        let transferCount = transfers |> Array.length
        let frequency = transferDates |> Schedule.detect Schedule.Reverse (UnitPeriod.Month 1)
        let schedule = Schedule.generate ((transferCount + 1) * multiple) Schedule.Reverse frequency |> Array.filter(fun dt -> dt >= termStart)
        let scheduleCount = schedule |> Array.length
        let lastWholeMonthBackIndex = 0
        let lastWholeUnitPeriodBackIndex = (scheduleCount - 1) % multiple
        let offset = (scheduleCount - 1) - ((transferCount - 1) * multiple) |> fun i -> Math.Floor(decimal i / decimal multiple) |> int
        [| 0 .. (transferCount - 1) |]
        |> Array.map(fun i ->
            let wholeUnitPeriods = decimal (i + offset)
            let remainingMonthDays = decimal (lastWholeUnitPeriodBackIndex - lastWholeMonthBackIndex) * 30m
            let remainingDays = decimal (schedule[lastWholeMonthBackIndex] - termStart).TotalDays
            let remainder =
                if multiple < 12 then
                    (remainingMonthDays + remainingDays) / (30m * decimal multiple)
                elif remainingDays = 0m then
                    decimal (lastWholeUnitPeriodBackIndex - lastWholeMonthBackIndex) / 12m
                else
                    decimal (schedule[lastWholeUnitPeriodBackIndex] - termStart).TotalDays / 365m
            transfers[i], { Quotient = wholeUnitPeriods; Remainder = remainder }
        )

    /// (b)(5)(iii) If the unit-period is a semimonth [...], the number of days between 2 dates shall be 30 times the number of full 
    /// months measured back from the later date, plus the number of remaining days. The number of full unit-periods and the remaining 
    /// fraction of a unit-period shall be determined by dividing such number of days by 15 in the case of a semimonthly unit-period 
    /// [...]. If the unit-period is a semimonth, the number of unit-periods per year shall be 24. [...]
    let semiMonthlyUnitPeriods termStart transfers =
        let transferDates = transfers |> Array.map(fun t -> t.Date)
        let transferCount = transfers |> Array.length
        let frequency = transferDates |> Schedule.detect Schedule.Reverse UnitPeriod.SemiMonth
        let schedule = Schedule.generate (transferCount + 2) Schedule.Reverse frequency |> Array.filter(fun dt -> dt >= termStart)
        let scheduleCount = schedule |> Array.length
        let offset = scheduleCount - transferCount
        [| 0 .. (transferCount - 1) |]
        |> Array.map(fun i ->
            let lastWholeMonthBackIndex = (i + offset) % 2
            let wholeUnitPeriods = decimal (i + offset - lastWholeMonthBackIndex)
            let fractional = decimal (schedule[lastWholeMonthBackIndex] - termStart).TotalDays / 15m |> fun d -> divRem d 1m
            transfers[i], { Quotient = wholeUnitPeriods + fractional.Quotient; Remainder = fractional.Remainder }
        )
        |> Array.map id

    /// (b)(5)(iv) If the unit-period is [...] a week, or a multiple of a week, the number of full unit-periods and the remaining 
    /// fractions of a unit-period shall be determined by dividing the number of days between the 2 given dates by the number of days 
    /// per unit-period. [...] If the unit-period is a week or a multiple of a week, the number of unit-periods per year shall be 
    /// 52 divided by the number of weeks per unit-period.
    let weeklyUnitPeriods (multiple: int) termStart transfers =
        let transferDates = transfers |> Array.map(fun t -> t.Date)
        let dr  = (daysBetween termStart (transferDates |> Array.head)) / (7m * decimal multiple) |> fun d -> divRem d 1m
        transfers
        |> Array.mapi(fun i t ->
            t, { Quotient = dr.Quotient + decimal i; Remainder = dr.Remainder }
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
        [| transfer, { Quotient = 1m; Remainder = 0m } |]

    /// map an array of transfers to an array of whole and fractional unit periods
    let mapUnitPeriods unitPeriod =
        match unitPeriod with
        | (UnitPeriod.NoInterval _) -> singleUnitPeriod
        | UnitPeriod.Day -> dailyUnitPeriods
        | (UnitPeriod.Week multiple) -> weeklyUnitPeriods multiple
        | UnitPeriod.SemiMonth -> semiMonthlyUnitPeriods
        | (UnitPeriod.Month multiple) -> monthlyUnitPeriods multiple

    /// overrides existing power function to take and return decimals
    let inline ( ** ) (var1: decimal) (var2: decimal) = decimal (Math.Pow(double var1, double var2))

    ///(b)(8) General equation.
    let generalEquation consummationDate firstFinanceChargeEarnedDate advances payments =
        let advanceDates = advances |> Array.map(fun a -> a.Date)
        let paymentDates = payments |> Array.map(fun p -> p.Date)
        let term = transactionTerm consummationDate firstFinanceChargeEarnedDate (paymentDates |> Array.last) (advanceDates |> Array.last)
        let unitPeriod = UnitPeriod.nearest term advanceDates paymentDates
        let unitPeriodsPerYear = UnitPeriod.numberPerYear unitPeriod
        let roughUnitPeriodRate =
            let paymentAverage = payments |> Array.averageBy(fun p -> p.Amount)
            let advanceTotal = advances |> Array.sumBy(fun a -> a.Amount)
            let paymentCount = payments |> Array.length |> decimal
            ((paymentAverage / advanceTotal) * (paymentCount / 12m)) / unitPeriodsPerYear
        let unitPeriodRate =
            let eq = [| advances[0].Amount, 0m, 0m |]
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
            Array.unfold(fun i -> //The percentage rate of finance charge per unit-period, expressed as a decimal equivalent.
                let aa = eq |> Array.sumBy(fun (a, e, q) -> a / ((1m + (e * i)) * ((1m + i) ** q)))
                let pp = ft |> Array.sumBy(fun (p, f, t) -> p / ((1m + (f * i)) * ((1m + i) ** t)))
                if Math.Round(pp - aa, 10) = 0m then
                    None
                else
                    Some (i, i * ((pp / aa) ** 2m))
            ) roughUnitPeriodRate
            |> Array.last
        annualPercentageRate unitPeriodRate unitPeriodsPerYear

    /// calculates the APR to a given precision for a single-advance transaction where the consummation date, first finance-charge earned date and
    /// advance date are all the same
    let calculate (precision: int) advanceAmount advanceDate payments =
        let advances = [| { TransferType = Advance; Date = advanceDate; Amount = advanceAmount } |]
        generalEquation advanceDate advanceDate advances payments
        |> fun apr -> Math.Round(apr, precision)
