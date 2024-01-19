module App

open Client
open Fable.Core.JsInterop
open Fable.Import
open Elmish
open Elmish.React
open Elmish.HMR
open Shared
open Thoth.Json
open Fable.Core
open Elmish.Navigation
open Pages

type PageModel =
    | HomePageModel of Home.Model
    | LoginModel of Login.Model
    | NotFoundModel
    | WishListModel of WishList.Model

type Model = {
    MenuModel : Menu.Model
    PageModel : PageModel
}

/// The composed set of messages that update the state of the application
type Msg =
    | AppHydrated
    | WishListMsg of WishList.Msg
    | HomePageMsg of Home.Msg
    | LoginMsg of Login.Msg
    | LoggedIn of UserData
    | LoggedOut
    | StorageFailure of exn
    | Logout of unit


// VIEW

open Fable.React
open Styles

let view model dispatch =
    div [ Key "Application" ] [
        Menu.view { Model = model.MenuModel; OnLogout = (Logout >> dispatch) }
        hr []

        div [ centerStyle "column" ] [
            match model.PageModel with
            | HomePageModel model ->
                yield Home.view model
            | NotFoundModel ->
                yield div [] [ str "The page is not available." ]
            | LoginModel m ->
                yield Login.view { Model = m; Dispatch = (LoginMsg >> dispatch) }
            | WishListModel m ->
                yield WishList.view { Model = m; Dispatch = (WishListMsg >> dispatch) }
        ]
    ]

let handleNotFound (model: Model) =
    JS.console.error("Error parsing url: " + Browser.Dom.window.location.href)
    ( model, Navigation.modifyUrl (toPath Page.NotFound) )

/// The navigation logic of the application given a page identity parsed from the .../#info
/// information in the URL.
let urlUpdate (result:Page option) (model:Model) =
    match result with
    | None ->
        handleNotFound model

    | Some Page.NotFound ->
        { model with PageModel = NotFoundModel }, Cmd.none

    | Some Page.Login ->
        let m, cmd = Login.init model.MenuModel.User
        { model with PageModel = LoginModel m }, Cmd.map LoginMsg cmd

    | Some Page.WishList ->
        match model.MenuModel.User with
        | Some user ->
            let m, cmd = WishList.init user.UserName user.Token
            { model with PageModel = WishListModel m }, Cmd.map WishListMsg cmd
        | _ ->
            model, Cmd.OfFunc.result (Logout ())

    | Some Page.Home ->
        let subModel, cmd = Home.init()
        { model with PageModel = HomePageModel subModel }, Cmd.map HomePageMsg cmd


let loadUser () : UserData option =
    let userDecoder = Decode.Auto.generateDecoder<UserData>()
    match LocalStorage.load userDecoder "user" with
    | Ok user -> Some user
    | Error _ -> None

let hydrateModel (json:string) (page: Page) =
    // The page was rendered server-side and now react client-side kicks in.
    // If needed, the model could be fixed up here.
    // In this case we just deserialize the model from the json and don't need to to anything special.
    let model: Model = Decode.Auto.unsafeFromString(json)
    match page, model.PageModel with
    | Page.Home, HomePageModel subModel when subModel.WishList <> None ->
        Some model
    | Page.Login, LoginModel _ ->
        Some model
    | Page.WishList, WishListModel _ ->
        Some model
    | _ ->
        None


let init page =
    let defaultModel () =
        // no SSR
        let model =
            { MenuModel = { User = loadUser(); RenderedOnServer = false }
              PageModel = HomePageModel Home.Model.Empty }

        urlUpdate page model

    // was the page rendered server-side?
    let stateJson: string option = !!Browser.Dom.window?__INIT_MODEL__

    match stateJson, page with
    | Some json, Some page ->
        // SSR -> hydrate the model
        match hydrateModel json page with
        | Some model ->
            { model with MenuModel = { model.MenuModel with User = loadUser() } }, Cmd.ofMsg AppHydrated
        | _ ->
            defaultModel()
    | _ ->
        defaultModel()


let update msg model =
    match msg, model.PageModel with
    | StorageFailure e, _ ->
        printfn $"Unable to access local storage: %A{e}"
        model, Cmd.none

    | HomePageMsg msg, HomePageModel m ->
        let m, cmd = Home.update msg m

        { model with
            PageModel = HomePageModel m }, Cmd.map HomePageMsg cmd

    | HomePageMsg _, _ -> model, Cmd.none

    | LoginMsg msg, LoginModel m ->
        match msg with
        | Login.Msg.LoginSuccess newUser ->
            model, Cmd.OfFunc.either (LocalStorage.save "user") newUser (fun _ -> LoggedIn newUser) StorageFailure
        | _ ->
            let m, cmd = Login.update msg m

            { model with
                PageModel = LoginModel m }, Cmd.map LoginMsg cmd

    | LoginMsg _, _ -> model, Cmd.none

    | WishListMsg msg, WishListModel m ->
        let m, cmd = WishList.update msg m
        { model with
            PageModel = WishListModel m }, Cmd.map WishListMsg cmd

    | WishListMsg _, _ ->
        model, Cmd.none

    | AppHydrated, _ ->
        { model with MenuModel = { model.MenuModel with RenderedOnServer = false }}, Cmd.none

    | LoggedIn newUser, _ ->
        let nextPage = Page.WishList
        { model with MenuModel = { model.MenuModel with User = Some newUser }},
        Navigation.newUrl (toPath nextPage)

    | LoggedOut, _ ->
        let subModel, cmd = Home.init()
        { MenuModel = { model.MenuModel with User = None }
          PageModel = HomePageModel subModel },
        Cmd.batch [
            Navigation.newUrl (toPath Page.Home)
            Cmd.map HomePageMsg cmd
        ]

    | Logout(), _ ->
        model, Cmd.OfFunc.either LocalStorage.delete "user" (fun _ -> LoggedOut) StorageFailure

open Elmish
open Elmish.Navigation
open Elmish.React
open Pages

open Fable.Core.JsInterop

importSideEffects "./index.css"

#if DEBUG
open Elmish.Debug
open Elmish.HMR
#endif

Program.mkProgram Index.init Index.update Index.view
|> Program.toNavigable urlParser urlUpdate
|> Program.withConsoleTrace
|> Program.withReactHydrate "elmish-app"
|> Program.withDebugger
|> Program.run