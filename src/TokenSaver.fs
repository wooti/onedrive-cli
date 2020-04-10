module TokenSaver
open Microsoft.Identity.Client
open System.IO

/// Register the token serialiser with the cache
/// This serialiser is not secure, and might need replacing with Microsoft's solution for a secure token cache
/// See: https://github.com/AzureAD/microsoft-authentication-extensions-for-dotnet/wiki/Cross-platform-Token-Cache
let register (tokenCache : ITokenCache) cacheFile =

    let locker = obj ()
    let asDelegate f = new TokenCacheCallback (f)

    let beforeAccess (args : TokenCacheNotificationArgs) = 
        lock locker (fun () ->
            if File.Exists cacheFile then
                args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cacheFile)))

    let afterAccess (args : TokenCacheNotificationArgs) =
        lock locker (fun () ->
            if args.HasStateChanged then
                File.WriteAllBytes(cacheFile,args.TokenCache.SerializeMsalV3()))
        
    tokenCache.SetBeforeAccess (beforeAccess |> asDelegate )
    tokenCache.SetAfterAccess (afterAccess |> asDelegate)