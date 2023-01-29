module HtmlApiGenerator

open FSharp.Text.TypedTemplateProvider
open W3schoolScrape

let [<Literal>] HtmlApiTemplate = """
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was auto generated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Vide

open System.Runtime.CompilerServices
open Browser.Types
open Vide

module HtmlElementBuilders =
    type HTMLGlobalAttrsVoidElementBuilder<'v,'n when 'n :> HTMLElement>(tagName, resultSelector) =
        inherit HTMLVoidElementBuilder<'v,'n>(tagName, resultSelector)

    type HTMLGlobalAttrsContentElementBuilder<'n when 'n :> HTMLElement>(tagName) =
        inherit HTMLContentElementBuilder<'n>(tagName)

    {{for builder in builders}}
    type {{builder.name}}() =
        inherit {{builder.inheritorName}}<{{builder.inheritorGenTypArgs}}>
            (
                {{builder.ctorBaseArgs}}
            )
    {{end}}

open HtmlElementBuilders

{{for ext in builderExtensions}}
[<Extension>]
type {{ext.builderName}}Extensions =
    class
        // Attributes
        {{for attr in ext.attributes}}
{{attr.xmlDoc}}
        [<Extension>]
        static member {{attr.memberName}}(this: {{ext.builderParamTypeAnnotation}}, value: {{attr.typ}}) =
            this.OnEval(fun x ctx -> x.setAttribute("{{attr.name}}", value{{attr.toString}}))
        {{end}}
    
        // Events
        {{for evt in ext.events}}
{{evt.xmlDoc}}
        [<Extension>]
        static member {{evt.memberName}}(this: {{ext.builderParamTypeAnnotation}}, handler) =
            this.OnEval(fun x ctx -> x.{{evt.name}} <- Event.handle x ctx handler)

{{evt.xmlDoc}}
        [<Extension>]
        static member {{evt.memberName}}(this: #HTMLGlobalAttrsVoidElementBuilder<_,_>, ?requestEvaluation: bool) =
            this.OnEval(fun x ctx -> x.{{evt.name}} <- Event.handle x ctx (fun args ->
                args.requestEvaluation <- defaultArg requestEvaluation true))
        {{end}}
    end
{{end}}

type Html =
    {{for builder in builders}}
{{builder.xmlDoc}}
    static member inline {{builder.name}} = HtmlElementBuilders.{{builder.name}}(){{builder.pipedConfig}}
    {{end}}
"""

type Api = Template<HtmlApiTemplate>


let htmlGlobalAttrsElementBuilderName = "HTMLGlobalAttrsElementBuilder"

let generate (elements: Element list) (globalAttrs: Attr list) (globalEvents: Evt list) =
    let makeCodeDoc (desc: string) indent =
        desc.Split('\n')
        |> Array.map (fun s ->
            let indent = String.replicate indent "    "
            $"{indent}/// {s}")
        |> String.concat "\n"

    let builders =
        [ for elem in elements do
            let ctorBaseArg,inheritorGenTypArgs,inheritorName,pipedConfig =
                match elem.elementType with
                | Content -> 
                    $""" "{elem.tagName}" """,
                    elem.domInterfaceName,
                    "HTMLGlobalAttrsContentElementBuilder",
                    ""
                | Void voidType ->
                    let valueTypeName,createResult,pipedConfig = 
                        if voidType.hasReturnValue
                        then 
                            "InputResult",
                            "InputResult(node)", 
                            ".oninput()"
                        else
                            "VoidResult",
                            "()", 
                            ""

                    $""" "{elem.tagName}", fun node -> {createResult} """,
                    $"{valueTypeName}, {elem.domInterfaceName}",
                    "HTMLGlobalAttrsVoidElementBuilder",
                    pipedConfig
            Api.builder(
                ctorBaseArg, 
                inheritorGenTypArgs, 
                inheritorName, 
                elem.fsharpName,
                pipedConfig,
                makeCodeDoc elem.desc 1
            )
        ]
    
    let builderExtensions =
        let makeAttrs (attrs: Attr list) =
            [ for attr in attrs do
                let typ,toString = "string", ""
                    // TODO
                    //match attr.types with
                    //| AttrTyp.Text -> "string", ""
                    //| AttrTyp.Boolean -> "bool", ".ToString()"
                    //| AttrTyp.Enum labels -> "string", ""
                Api.attr(
                    attr.fsharpName, 
                    attr.name, 
                    toString, 
                    typ,
                    makeCodeDoc attr.desc 2
                )
            ]
        let makeEvts (evts: Evt list) =
            [ for evt in evts do
                Api.evt(evt.name, evt.name, makeCodeDoc evt.desc 2)
            ]

        [
            Api.ext(
                makeAttrs globalAttrs,
                "HTMLGlobalAttrsVoidElementBuilder",
                "#HTMLGlobalAttrsVoidElementBuilder<_,_>",
                makeEvts globalEvents
            )
            Api.ext(
                makeAttrs globalAttrs,
                "HTMLGlobalAttrsContentElementBuilder",
                "#HTMLGlobalAttrsContentElementBuilder<_>",
                makeEvts globalEvents
            )

            for elem in elements do
                Api.ext(
                    makeAttrs elem.attrs, 
                    elem.fsharpName, 
                    $"#{elem.fsharpName}", 
                    []
                )
        ]

    let root = Api.Root(builderExtensions, builders)

    Api.Render(root)