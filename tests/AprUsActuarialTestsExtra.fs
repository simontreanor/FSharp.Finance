namespace FSharp.Finance.Personal.Tests

open System
open Xunit
open FsUnit.Xunit
open System.Text.Json

open FSharp.Finance.Personal

module AprActuarialTestsExtra =

    open Apr

    type AprUsActuarialTestItemDto = {
        StartDate: Date
        Principal: decimal
        PaymentAmount: decimal
        PaymentDates: Date array
        ExpectedApr: decimal
        ActualApr: decimal
    }

    type AprUsActuarialTestItem = {
        StartDate: Date
        Principal: int64<Cent>
        PaymentAmount: int64<Cent>
        PaymentDates: Date array
        ExpectedApr: Percent
        ActualApr: Percent
    }

    // let aprUsActuarialTestData =
    //     IO.File.ReadAllText $"{__SOURCE_DIRECTORY__}/../tests/io/in/AprUsActuarialTestData.json"
    //     |> JsonSerializer.Deserialize<AprUsActuarialTestItemDto array>
    //     |> Array.map(fun ssi ->
    //         let principal = Cent.fromDecimal ssi.Principal
    //         let paymentAmount = Cent.fromDecimal ssi.PaymentAmount
    //         let payments = ssi.PaymentDates |> Array.map(fun d -> { TransferType = Payment; TransferDate = d; Amount = paymentAmount })
    //         let actualApr = calculate (CalculationMethod.UsActuarial 8) principal ssi.StartDate payments |> Percent.round 4
    //         {
    //             StartDate = ssi.StartDate
    //             Principal = principal
    //             PaymentAmount = paymentAmount
    //             PaymentDates = ssi.PaymentDates
    //             ExpectedApr = Percent ssi.ExpectedApr
    //             ActualApr = actualApr
    //         }
    //     )
    //     |> toMemberData

    // [<Theory>]
    // [<MemberData(nameof(aprUsActuarialTestData))>]
    // let ``Actual APRs match expected APRs under the US actuarial method`` testItem =
    //     testItem.ActualApr |> should equal testItem.ExpectedApr
