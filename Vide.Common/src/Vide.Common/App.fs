﻿namespace Vide

type VideApp<'v,'s,'c>(content: Vide<'v,'s,'c>, ctxCtor: unit -> 'c, ctxFin: 'c -> unit) =
    let mutable currValue = None
    let mutable currentState = None
    let mutable isEvaluating = false
    let mutable hasPendingEvaluationRequests = false
    let mutable evaluationCount = 0uL
    let mutable suspendEvaluation = false
    let mutable onEvaluated: (VideApp<'v,'s,'c> -> 'v -> 's option -> unit) option = None

    interface IEvaluationManager with
        member this.RequestEvaluation() =
            if suspendEvaluation then
                hasPendingEvaluationRequests <- true
            else
                // During an evaluation, requests for another evaluation can
                // occur, which have to be handled as _subsequent_ evaluations!
                let rec eval () =
                    do
                        hasPendingEvaluationRequests <- false
                        isEvaluating <- true
                    let value,newState = 
                        let gc = { evaluationManager = this.EvaluationManager } 
                        Debug.print 0 "-----------------------------------------"
                        let ctx = ctxCtor ()
                        let res = content currentState gc ctx
                        do ctxFin ctx
                        res
                    do
                        currValue <- Some value
                        currentState <- newState
                        isEvaluating <- false
                        evaluationCount <- evaluationCount + 1uL
                    do onEvaluated |> Option.iter (fun x -> x this value currentState)
                    if hasPendingEvaluationRequests then
                        eval ()
                do
                    match isEvaluating with
                    | true -> hasPendingEvaluationRequests <- true
                    | false -> eval ()
        member _.Suspend() =
            do suspendEvaluation <- true
        member this.Resume() =
            do suspendEvaluation <- false
            if hasPendingEvaluationRequests then
                (this :> IEvaluationManager).RequestEvaluation()
        member _.IsEvaluating = isEvaluating
        member _.HasPendingEvaluationRequests = hasPendingEvaluationRequests
        member _.EvaluationCount = evaluationCount

    member this.EvaluationManager = this :> IEvaluationManager
    member _.CurrentState = currentState
    member _.SetOnEvaluated(value) = onEvaluated <- value

type VideAppFactory<'c>(ctxCtor, ctxFin) =
    let start (app: VideApp<_,_,'c>) =
        do app.EvaluationManager.RequestEvaluation()
        app
    member _.Create(content) : VideApp<_,_,'c> =
        VideApp(content, ctxCtor, ctxFin)
    member this.CreateWithUntypedState(content) : VideApp<_,_,'c> =
        let content =
            ensureVide <| fun (s: obj option) gc ctx ->
                let typedS = s |> Option.map (fun s -> s :?> 's)
                let v,s = content typedS gc ctx
                let untypedS = s |> Option.map (fun s -> s :> obj)
                v,untypedS
        this.Create(content)
    member this.CreateAndStart(content) : VideApp<_,_,'c> =
        this.Create(content) |> start
    member this.CreateAndStartWithUntypedState(content) : VideApp<_,_,'c> =
        this.CreateWithUntypedState(content) |> start
