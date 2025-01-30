module AsyncResult

    // Define the AsyncResult type
    type AsyncResult<'T, 'Error> = Async<Result<'T, 'Error>>

    // Define the builder type for asyncResult computation expression
    type AsyncResultBuilder() =
        member _.Return(value: 'T) : AsyncResult<'T, 'Error> =
            async { return Ok value }

        member _.ReturnFrom(asyncResult: AsyncResult<'T, 'Error>) : AsyncResult<'T, 'Error> =
            asyncResult

        member _.Bind(asyncResult: AsyncResult<'T, 'Error>, f: 'T -> AsyncResult<'U, 'Error>) : AsyncResult<'U, 'Error> =
            async {
                match! asyncResult with
                | Ok value -> return! f value
                | Error error -> return Error error
            }

        member _.Zero() : AsyncResult<unit, 'Error> =
            async { return Ok () }

        member _.Delay(f: unit -> AsyncResult<'T, 'Error>) : AsyncResult<'T, 'Error> =
            async { return! f () }

        member _.Combine(asyncResult1: AsyncResult<unit, 'Error>, asyncResult2: AsyncResult<'T, 'Error>) : AsyncResult<'T, 'Error> =
            async {
                match! asyncResult1 with
                | Ok () -> return! asyncResult2
                | Error error -> return Error error
            }

    // Create an instance of the builder
    let asyncResult = AsyncResultBuilder()

    // Helper function to lift a Result into AsyncResult
    let ofResult (result: Result<'T, 'Error>) : AsyncResult<'T, 'Error> =
        async { return result }

    // Helper function to lift an Async into AsyncResult
    let ofAsync (asyncOp: Async<'T>) : AsyncResult<'T, 'Error> =
        async {
            let! result = asyncOp
            return Ok result
        }

    // Helper function to lift an Async<Result> into AsyncResult
    let ofAsyncResult (asyncResultOp: Async<Result<'T, 'Error>>) : AsyncResult<'T, 'Error> =
        asyncResultOp