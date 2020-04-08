module TokenSaver

open Microsoft.Identity.Client
open System.IO

let register (tokenCache : ITokenCache) cacheFile =

    let locker = obj ()

    let asDelegate f =
        new TokenCacheCallback (f)

    let beforeAccess (args : TokenCacheNotificationArgs) = 
        lock locker (fun () ->
            if File.Exists cacheFile then
                args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cacheFile))
        )

    let afterAccess (args : TokenCacheNotificationArgs) =
        lock locker (fun () ->
            if args.HasStateChanged then
                File.WriteAllBytes(cacheFile,args.TokenCache.SerializeMsalV3())
        )
        
    tokenCache.SetBeforeAccess (beforeAccess |> asDelegate )
    tokenCache.SetAfterAccess (afterAccess |> asDelegate)