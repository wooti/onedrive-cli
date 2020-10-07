// https://fsharpforfunandprofit.com/posts/elevated-world-5/
module Async 

let map f xAsync = async {
    // get the contents of xAsync 
    let! x = xAsync 
    // apply the function and lift the result
    return f x
    }

let retn x = async {
    // lift x to an Async
    return x
    }

let apply fAsync xAsync = async {
    // start the two asyncs in parallel
    let! fChild = Async.StartChild fAsync
    let! xChild = Async.StartChild xAsync

    // wait for the results
    let! f = fChild
    let! x = xChild 

    // apply the function to the results
    return f x 
    }

let bind f xAsync = async {
    // get the contents of xAsync 
    let! x = xAsync 
    // apply the function but don't lift the result
    // as f will return an Async
    return! f x
}