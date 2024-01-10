namespace FSharp.Finance.Personal

open System

[<AutoOpen>]
module DateDay =

    [<Struct>]
    type Date =
        val Year: int
        val Month: int
        val Day: int
        new (year: int, month: int, day: int) = { Year = year; Month = month; Day = day }
        with
            member d.AddDays (i: int) = DateTime(d.Year, d.Month, d.Day).AddDays(float i) |> fun d -> Date(d.Year, d.Month, d.Day)
            member d.AddMonths i = DateTime(d.Year, d.Month, d.Day).AddMonths i |> fun d -> Date(d.Year, d.Month, d.Day)
            member d.AddYears i = DateTime(d.Year, d.Month, d.Day).AddYears i |> fun d -> Date(d.Year, d.Month, d.Day)
            override d.ToString() = DateTime(d.Year, d.Month, d.Day).ToString "yyyy-MM-dd"
            static member (-) (d1: Date, d2: Date) =  DateTime(d1.Year, d1.Month, d1.Day) - DateTime(d2.Year, d2.Month, d2.Day)
            static member DaysInMonth(year, month) = DateTime.DaysInMonth(year, month)

    /// the offset of a date from the start date, in days
    [<Measure>]
    type OffsetDay

    [<RequireQualifiedAccess>]
    module OffsetDay =
        /// convert an offset date to an offset day based on a given start date
        let fromDate (startDate: Date) (offsetDate: Date) = (offsetDate - startDate).Days * 1<OffsetDay>
        /// convert an offset day to an offset date based on a given start date
        let toDate (startDate: Date) (offsetDay: int<OffsetDay>) = startDate.AddDays(int offsetDay)


    /// a duration of a number of days
    [<Measure>] type DurationDay

    /// day of month, bug: specifying 29, 30, or 31 means the dates will track the specific day of the month where
    /// possible, otherwise the day will be the last day of the month; so 31 will track the month end; also note that it is
    /// possible to start with e.g. (2024, 02, 31) and this will yield 2024-02-29 29 2024-03-31 2024-04-30 etc.
    [<RequireQualifiedAccess>]
    module TrackingDay =
        /// create a date from a year, month, and tracking day
        let toDate y m td = Date(y, m, min (Date.DaysInMonth(y, m)) td)
