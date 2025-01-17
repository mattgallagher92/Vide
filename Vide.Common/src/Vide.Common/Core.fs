namespace Vide

type IEvaluationManager =
    abstract member RequestEvaluation: unit -> unit
    abstract member Suspend: unit -> unit
    abstract member Resume: unit -> unit
    abstract member IsEvaluating: bool
    abstract member HasPendingEvaluationRequests: bool
    abstract member EvaluationCount: uint64

type GlobalContext = { evaluationManager: IEvaluationManager }

// why we return 's option(!!) -> Because of else branch / zero
// -> TODO: Is that still valid, now that we explicitly use preserve/discard?
type Vide<'v,'s,'c> = 's option -> GlobalContext -> 'c -> 'v * 's option

[<AutoOpen>]
module TypingHelper =
    let ensureVide f : Vide<_,_,_> = f

module Debug =
    let mutable enabledDebugChannels : int list = []
    let print channel s = if enabledDebugChannels |> List.contains channel then printfn "%s" s

type AsyncState<'v> =
    {
        startedWorker: Async<'v>
        result: Ref<'v option>
    }

type AsyncBindResult<'v1,'v2> = 
    AsyncBindResult of comp: Async<'v1> * cont: ('v1 -> 'v2)

module Vide =

    // Preserves the first value given and discards subsequent values.
    let preserveValue x =
        ensureVide <| fun s gc ctx ->
            let s = s |> Option.defaultValue x
            s, Some s
    
    let preserveWith x =
        ensureVide <| fun s gc ctx ->
            let s = s |> Option.defaultWith x
            s, Some s
    
    // TODO: Think about which function is "global" and module-bound
    let map (proj: 'v1 -> 'v2) (v: Vide<'v1,'s,'c>) : Vide<'v2,'s,'c> =
        fun s gc ctx ->
            let v,s = v s gc ctx
            proj v, s
    
    // why 's and not unit? -> see comment in "VideBuilder.Zero"
    [<GeneralizableValue>]
    let zero<'c> : Vide<unit,unit,'c> =
        fun s gc ctx -> (),None

    // this "zero", but the form where state is variable
    // -> see comment in "VideBuilder.Zero"
    // -> we don't really need that, only as "elseForget" or cases
    // with similar semantics.
    ////[<GeneralizableValue>]
    ////let empty<'s,'c> : Vide<unit,'s,'c> =
    ////    fun s gc ctx -> (),None

    [<GeneralizableValue>]
    let context<'c> : Vide<'c,unit,'c> =
        fun s gc ctx -> ctx,None

module BuilderBricks =
    let bind<'v1,'s1,'v2,'s2,'c>
        (
            m: Vide<'v1,'s1,'c>,
            f: 'v1 -> Vide<'v2,'s2,'c>
        ) 
        : Vide<'v2,'s1 option * 's2 option,'c>
        =
        fun s gc ctx ->
            let ms,fs =
                match s with
                | None -> None,None
                | Some (ms,fs) -> ms,fs
            let mv,ms = m ms gc ctx
            let f = f mv
            let fv,fs = f fs gc ctx
            fv, Some (ms,fs)

    let return'<'v,'c>
        (x: 'v)
        : Vide<'v,unit,'c> 
        =
        fun s gc ctx -> x,None

    let yield'<'v,'s,'c>
        (v: Vide<'v,'s,'c>)
        : Vide<'v,'s,'c>
        =
        v

    // This zero (with "unit" as state) is required for multiple returns.
    // Another zero (with 's as state) is required for "if"s without an "else".
    // We cannot have both, which means: We cannot have "if"s without "else".
    // This is ok (and not unfortunate), because the developer has to make a
    // decision about what should happen: "elseForget" or "elsePreserve".
    let zero<'c>
        ()
        : Vide<unit,unit,'c>
        = Vide.zero<'c>

    let delay<'v,'s,'c>
        (f: unit -> Vide<'v,'s,'c>)
        : Vide<'v,'s,'c>
        =
        f()

    // TODO
    // those 2 are important for being able to combine
    // HTML elements, returns and asyncs in arbitrary order
    //member _.Combine
    //    (
    //        a: Vide<unit,'s1,'c>,
    //        b: Vide<'v,'s2,'c>
    //    )
    //    : Vide<'v,'s1 option * 's2 option,'c>
    //    =
    //    combine a b snd
    //member _.Combine
    //    (
    //        a: Vide<'v,'s1,'c>,
    //        b: Vide<unit,'s2,'c>
    //    )
    //    : Vide<'v,'s1 option * 's2 option,'c>
    //    =
    //    combine a b fst
    let combine<'v1,'s1,'v2,'s2,'c>
        (
           a: Vide<'v1,'s1,'c>,
           b: Vide<'v2,'s2,'c>
        )
        : Vide<'v2,'s1 option * 's2 option,'c>
        =
        fun s gc ctx ->
            let sa,sb =
                match s with
                | None -> None,None
                | Some (ms,fs) -> ms,fs
            let va,sa = a sa gc ctx
            let vb,sb = b sb gc ctx
            vb, Some (sa,sb)

    let for'<'a,'v,'s,'c when 'a: comparison>
        (
            input: seq<'a>,
            body: 'a -> Vide<'v,'s,'c>
        ) 
        : Vide<'v list, Map<'a, 's option>,'c>
        = 
        fun s gc ctx ->
            Debug.print 0 "FOR"
            let mutable currMap = s |> Option.defaultValue Map.empty
            let resValues,resStates =
                [ for x in input do
                    let matchingState = currMap |> Map.tryFind x |> Option.flatten
                    let v,s = 
                        let v = body x
                        v matchingState gc ctx
                    do currMap <- currMap |> Map.remove x
                    v, (x,s)
                ]
                |> List.unzip
            resValues, Some (resStates |> Map.ofList)


    // ---------------------
    // ASYNC
    // ---------------------
    
    module Async =
        let bind<'v1,'v2,'s,'c>
            (
                m: Async<'v1>,
                f: 'v1 -> Vide<'v2,'s,'c>
            ) : AsyncBindResult<'v1, Vide<'v2,'s,'c>>
            =
            AsyncBindResult(m, f)
    
        let delay
            (f: unit -> AsyncBindResult<'v1,'v2>)
            : AsyncBindResult<'v1,'v2>
            =
            f()
    
        let combine<'v,'x,'s1,'s2,'c>
            (
                a: Vide<'v,'s1,'c>,
                b: AsyncBindResult<'x, Vide<'v,'s2,'c>>
            )
            : Vide<'v, 's1 option * AsyncState<_> option * 's2 option, 'c>
            =
            fun s gc ctx ->
                let sa,comp,sb =
                    match s with
                    | None -> None,None,None
                    | Some (sa,comp,sb) -> sa,comp,sb
                // TODO: Really reevaluate here at this place?
                let va,sa = a sa gc ctx
                let v,comp,sb =
                    match comp with
                    | None ->
                        let (AsyncBindResult (comp,_)) = b
                        let result = ref None
                        do
                            let onsuccess res =
                                Debug.print 1 $"awaited result: {res}"
                                do result.Value <- Some res
                                do gc.evaluationManager.RequestEvaluation()
                            // TODO: global cancellation handler / ex / cancellation, etc.
                            let onexception ex = ()
                            let oncancel ex = ()
                            do Async.StartWithContinuations(comp, onsuccess, onexception, oncancel)
                        let comp = { startedWorker = comp; result = result }
                        va,comp,None
                    | Some comp ->
                        match comp.result.Value with
                        | Some v ->
                            let (AsyncBindResult (_,f)) = b
                            let b = f v
                            let vb,sb = b sb gc ctx
                            vb,comp,sb
                        | None ->
                            va,comp,sb
                v, Some (sa, Some comp, sb)

// TODO:
//    For value restriction and other resolution issues, it's better to
//    move these (remaining) builder methods "as close as possible" to the builder class that's
//    most specialized.
type VideBaseBuilder() =
    member _.Bind(m, f) = BuilderBricks.bind(m, f)
    member _.Zero() = BuilderBricks.zero()

    // ---------------------
    // ASYNC
    // ---------------------
    member _.Bind(m, f) = BuilderBricks.Async.bind(m, f)
    member _.Delay(f) = BuilderBricks.Async.delay(f)
    member _.Combine(a, b) = BuilderBricks.Async.combine(a, b)
    // TODO: async for

[<AutoOpen>]
module ControlFlow =
    type Branch2<'b1,'b2> =
        | B1Of2 of 'b1
        | B2Of2 of 'b2
    
    type Branch3<'b1,'b2,'b3> =
        | B1Of3 of 'b1
        | B2Of3 of 'b2
        | B3Of3 of 'b3
    
    type Branch4<'b1,'b2,'b3,'b4> =
        | B1Of4 of 'b1
        | B2Of4 of 'b2
        | B3Of4 of 'b3
        | B4Of4 of 'b4

[<AutoOpen>]
module Keywords =
    
    [<GeneralizableValue>]
    let elsePreserve<'s,'c> : Vide<unit,'s,'c> =
        fun s gc ctx -> (),s

    [<GeneralizableValue>]
    let elseForget<'s,'c> : Vide<unit,'s,'c> = 
        fun s gc ctx -> (),None
        //Vide.empty<'s,'c>
