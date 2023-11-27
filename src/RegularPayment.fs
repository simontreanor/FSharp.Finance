namespace FSharp.Finance.Personal

open System

/// functions for generating a regular payment schedule, with payment amounts, interest and APR
module RegularPayment =

    /// 
    [<Struct>]
    type ScheduleItem = {
        Day: int<Day>
        Advance: int<Cent>
        Payment: int<Cent>
        Interest: int<Cent>
        CumulativeInterest: int<Cent>
        Principal: int<Cent>
        PrincipalBalance: int<Cent>
    }

    type Schedule = {
        Items: ScheduleItem array
        FinalPaymentDay: int<Day>
        LevelPayment: int<Cent>
        FinalPayment: int<Cent>
        PaymentTotal: int<Cent>
        PrincipalTotal: int<Cent>
        InterestTotal: int<Cent>
        Apr: Percent
        CostToBorrowingRatio: Percent
    }

    [<Struct>]
    type ScheduleParameters = {
        StartDate: DateTime
        Principal: int<Cent>
        ProductFees: ProductFees voption
        InterestRate: InterestRate
        InterestCap: InterestCap voption
        UnitPeriodConfig: UnitPeriod.Config
        PaymentCount: int
    }

    let calculateSchedule sp =
        if sp.PaymentCount = 0 then ValueNone else
        let paymentDates = Schedule.generate sp.PaymentCount Schedule.Forward sp.UnitPeriodConfig
        let finalPaymentDate = paymentDates |> Array.max
        let finalPaymentDay = (finalPaymentDate.Date - sp.StartDate.Date).Days
        let paymentCount = paymentDates |> Array.length
        let productFees = productFeesTotal sp.Principal sp.ProductFees
        let dailyInterestRate = sp.InterestRate |> dailyInterestRate
        let interestCap = sp.InterestCap |> calculateInterestCap sp.Principal
        let roughPayment = (decimal sp.Principal + (decimal sp.Principal * Percent.toDecimal dailyInterestRate * decimal finalPaymentDay)) / decimal paymentCount
        let advance = { Day = 0<Day>; Advance = sp.Principal + productFees; Payment = 0<Cent>; Interest = 0<Cent>; CumulativeInterest = 0<Cent>; Principal = 0<Cent>; PrincipalBalance = sp.Principal + productFees }
        let tolerance = paymentCount * 1<Cent> // tolerance is the payment count expressed as a number of cents // to-do: check if this is too tolerant with large payment counts
        let paymentDays = paymentDates |> Array.map(fun dt -> (dt.Date - sp.StartDate.Date).Days * 1<Day>)
        roughPayment
        |> Array.solve(fun roughPayment' ->
            if roughPayment' = 0m then ValueNone else
            let schedule =
                paymentDays
                |> Array.scan(fun si d ->
                    let interest = decimal si.PrincipalBalance * Percent.toDecimal dailyInterestRate * decimal (d - si.Day) |> Cent.floor
                    let interest' = si.CumulativeInterest + interest |> fun i -> if i >= interestCap then interestCap - si.CumulativeInterest else interest
                    let payment = Cent.floor roughPayment'
                    let principalPortion = payment - interest
                    {
                        Day = d
                        Advance = 0<Cent>
                        Payment = payment
                        Interest = interest'
                        CumulativeInterest = si.CumulativeInterest + interest'
                        Principal = principalPortion
                        PrincipalBalance = si.PrincipalBalance - principalPortion
                    }
                ) advance
            let principalBalance = schedule |> Array.last |> fun psi -> psi.PrincipalBalance
            if principalBalance >= -tolerance && principalBalance <= 0<Cent> then
                ValueSome (schedule, 0m)
            else
                ValueSome (schedule, roughPayment' + (decimal principalBalance / decimal paymentCount))
        ) 100
        |> ValueOption.map(fun sii ->
            let finalPaymentDay = (finalPaymentDate.Date - sp.StartDate.Date).Days * 1<Day>
            let items =
                sii |> Array.map(fun si ->
                    if si.Day = finalPaymentDay then { si with Payment = si.Payment + si.PrincipalBalance; Principal = si.Principal + si.PrincipalBalance; PrincipalBalance = si.PrincipalBalance - si.PrincipalBalance }
                    else si
                )
            let principalTotal = items |> Array.sumBy _.Principal
            let interestTotal = items |> Array.sumBy _.Interest
            {
                Items = items
                FinalPaymentDay = finalPaymentDay
                LevelPayment = items |> Array.countBy _.Payment |> Array.maxByOrDefault snd fst 0<Cent>
                FinalPayment = items |> Array.last |> _.Payment
                PaymentTotal = items |> Array.sumBy _.Payment
                PrincipalTotal = principalTotal
                InterestTotal = interestTotal
                Apr =
                    items
                    |> Array.filter(fun si -> si.Payment > 0<Cent>)
                    |> Array.map(fun si -> { Apr.TransferType = Apr.Payment; Apr.Date = sp.StartDate.AddDays(float si.Day); Apr.Amount = si.Payment })
                    |> Apr.calculate Apr.UsActuarial 8 sp.Principal sp.StartDate
                CostToBorrowingRatio =
                    if principalTotal = 0<Cent> then Percent 0m else
                    decimal (productFees + interestTotal) / decimal principalTotal |> Percent.fromDecimal |> Percent.round 6
            }
        )
