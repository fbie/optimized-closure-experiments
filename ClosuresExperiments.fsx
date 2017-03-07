#nowarn "63"

open System

/// A standard tail-recursive reduce function on cons-lists.
let rec reduceStd f e = function
    | [] -> e
    | x :: xs -> reduceStd f (f e x) xs


open OptimizedClosures

/// The same tail-recursive reduce function, but we "adapt" the
/// reduction function first and invoke it via its member function
/// Invoke().
let reduceOpt f e xs =
    let rec red (f : FSharpFunc<_, _, _>) e = function
        | [] -> e
        | x :: xs -> red f (f.Invoke(e, x)) xs

    red (FSharpFunc<_, _, _>.Adapt f) e xs


let n = Convert.ToInt32 fsi.CommandLineArgs.[2]
let xs = List.init n id
let sum () = reduceStd (+) 0 xs
let sumOpt() = reduceOpt (+) 0 xs


#r "MathNet.Numerics.dll"
#r "LambdaMicrobenchmarking.dll"


open LambdaMicrobenchmarking

Script.Of("sum",    Func<int32> sum)
      .Of("sumOpt", Func<int32> sumOpt)
      .WithHead()
      .RunAll() |> ignore
