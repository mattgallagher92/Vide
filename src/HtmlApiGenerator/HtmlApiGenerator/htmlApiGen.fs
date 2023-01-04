module HtmlApiGenerator

open FSharp.Text.TypedTemplateProvider

let [<Literal>] HtmlApiTemplate = """
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was auto generated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Vide

open Browser.Types
open Vide

module HtmlElementBuilders =
    {{for elem in elements}}
    type {{elem.typeName}}() =
        inherit HTMLElementBuilder<{{elem.domInterfaceName}}>("{{elem.tagName}}")
        {{for attr in elem.attributes}}member this.{{attr.memberName}}(value: {{attr.typ}}) = this.OnEval(fun x -> x.setAttribute("{{attr.name}}", value{{attr.toString}}))
        {{end}}
        {{for evt in elem.events}}member this.{{evt.name}}(handler) = this.OnInit(fun x -> x.{{evt.name}} <- handler)
        {{end}}
    {{end}}

type Html =
    static member inline nothing = HtmlBase.nothing ()
    static member inline text text = HtmlBase.text text
    // ---
    {{for elem in elements}}static member inline {{elem.typeName}} = HtmlElementBuilders.{{elem.tagName}}()
    {{end}}
"""

type Api = Template<HtmlApiTemplate>

let attrNameCorrections =
    [
        "class", "className"
        "type", "type'"
        "as", "as'"
        "default", "default'"
        "for", "for'"
        "open", "open'"
        "http-equiv", "httpEquiv"
        "moz-opaque", "mozOpaque"
        "accept-charset", "acceptCharset"
    ]

let attrExcludes =
    [
        "data-*"
    ]
        
let elemNameCorrections =
    [
        "base", "base'"
    ]

let elemExcludes =
    [
        "base"
        "data"
        "time"
        "picture"
        "meter"
        "output"
        "details"
        "dialog"
        "slot"
        "template"
        "portal"
    ]

let globalEvents =
    [
        "onabort"
        //"onautocomplete"
        //"onautocompleteerror"
        "onblur"
        //"oncancel"
        "oncanplay"
        "oncanplaythrough"
        "onchange"
        "onclick"
        //"onclose"
        "oncontextmenu"
        "oncuechange"
        "ondblclick"
        "ondrag"
        "ondragend"
        "ondragenter"
        "ondragleave"
        "ondragover"
        "ondragstart"
        "ondrop"
        "ondurationchange"
        "onemptied"
        "onended"
        "onerror"
        "onfocus"
        "oninput"
        //"oninvalid"
        "onkeydown"
        "onkeypress"
        "onkeyup"
        "onload"
        "onloadeddata"
        "onloadedmetadata"
        "onloadstart"
        "onmousedown"
        "onmouseenter"
        "onmouseleave"
        "onmousemove"
        "onmouseout"
        "onmouseover"
        "onmouseup"
        "onmousewheel"
        "onpause"
        "onplay"
        "onplaying"
        "onprogress"
        "onratechange"
        "onreset"
        //"onresize"
        "onscroll"
        "onseeked"
        "onseeking"
        "onselect"
        //"onshow"
        //"onsort"
        "onstalled"
        "onsubmit"
        "onsuspend"
        "ontimeupdate"
        //"ontoggle"
        "onvolumechange"
        "onwaiting"
    ]

let generate (elements: MdnScrape.Element list) = 
    let correctWith altNames name =
        altNames 
        |> List.tryFind (fun x -> fst x = name)
        |> Option.map snd
        |> Option.defaultValue name

    let root =
        Api.Root(
            [
                for elem in elements |> List.filter (fun e -> elemExcludes |> List.contains e.name |> not)  do
                    let events = globalEvents |> List.map Api.evt
                    let attrs =
                        [ for attr in elem.attrs.attrs |> List.distinctBy (fun a -> a.name) do
                            let memberName = attr.name |> correctWith attrNameCorrections
                            let typ,toString =
                                match attr.typ with
                                | MdnScrape.AttrTyp.Dotnet typ ->
                                    let toString =
                                        match typ with
                                        | t when t = typeof<string> -> ""
                                        | _ -> ".ToString()"
                                    typ.FullName, toString
                                | MdnScrape.AttrTyp.Enum labels -> "string", ""
                                | MdnScrape.AttrTyp.Choice labels -> "string", ""
                            Api.attr(toString, attr.name, typ, memberName)
                        ]
                        |> List.filter (fun attr -> attrExcludes |> List.contains attr.name |> not)
                    let typeName = elem.name |> correctWith elemNameCorrections
                    Api.elem(elem.name, typeName, events, attrs, elem.domInterfaceName)
            ]
        )
    Api.Render(root)
