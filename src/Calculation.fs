namespace FSharp.Finance.Personal

open System

/// convenience functions and options to help with calculations
module Calculation =

    open DateDay

    /// the intended purpose of the calculation
    [<RequireQualifiedAccess; Struct>]
    type IntendedPurpose =
        /// intended to quote a calculated amount, e.g. for settlement purposes
        | Settlement of SettlementDay: int<OffsetDay> voption
        /// intended just for information, e.g. to view the current status of a loan
        | Statement

    /// holds the result of a division, separated into quotient and remainder
    [<Struct>]
    type DivisionResult = {
        /// the whole number resulting from a division
        Quotient: int
        /// the fractional number remaining after subtracting the quotient from the result
        Remainder: decimal
    }

    /// computes the quotient and remainder of two decimal values
    let divRem (left: decimal) (right: decimal) = 
        left % right
        |> fun r -> { Quotient = int(left - r); Remainder = r }

    /// rounds to the nearest number, and when a number is halfway between two others, it's rounded toward the nearest number that's towards 0L<Cent>
    let roundMidpointTowardsZero m =
        divRem m 1m
        |> fun dr -> if dr.Remainder <= 0.5m then dr.Quotient else dr.Quotient + 1

    /// the type of rounding, specifying midpoint-rounding where necessary
    [<Struct>]
    type Rounding =
        /// round up to the specified precision (= ceiling)
        | RoundUp
        /// round down to the specified precision (= floor)
        | RoundDown
        /// round up or down to the specified precision based on the given midpoint rounding rules
        | Round of MidpointRounding
        with
            /// derive a rounded value from a decimal according to the specified rounding method
            static member round rounding (m: decimal) =
                match rounding with
                | ValueSome (RoundDown) -> floor m
                | ValueSome (RoundUp) -> ceil m
                | ValueSome (Round mpr) -> Math.Round(m, 0, mpr)
                | ValueNone -> m

    /// raises a decimal to an int power
    let powi (power: int) (base': decimal) = decimal (Math.Pow(double base', double power))

    /// raises a decimal to a decimal power
    let powm (power: decimal) (base': decimal) = Math.Pow(double base', double power)

    /// round a value to n decimal places
    let roundTo rounding (places: int) (m: decimal) =
        match rounding with
        | ValueSome (RoundDown) -> 10m |> powi places |> fun f -> if f = 0m then 0m else m * f |> floor |> fun m -> m / f
        | ValueSome (RoundUp) -> 10m |> powi places |> fun f -> if f = 0m then 0m else m * f |> ceil |> fun m -> m / f
        | ValueSome (Round mpr) -> Math.Round(m, places, mpr)
        | ValueNone -> m

    /// a holiday, i.e. a period when no interest and/or charges are accrued
    [<RequireQualifiedAccess; Struct>]
    type DateRange = {
        /// the first date of the holiday period
        Start: Date
        /// the last date of the holiday period
        End: Date
    }

    /// determines whether a pending payment has timed out
    let timedOut paymentTimeout (asOfDay: int<OffsetDay>) (paymentDay: int<OffsetDay>) =
        (int asOfDay - int paymentDay) * 1<DurationDay> > paymentTimeout

    /// a fraction expressed as a numerator and denominator
    [<Struct>]
    type Fraction = {
        Numerator: int
        Denominator: int
    }
    with
        member x.toDecimal = if x.Denominator = 0 then 0m else decimal x.Numerator / decimal x.Denominator
