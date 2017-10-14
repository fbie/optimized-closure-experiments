2017-10-14, v. 0.2

# Optimized Closures in F# #

While implementing a data structure that represents high-level contiguous arrays, we ran into some strange performance behavior. After digging around in the F# core libraries, we found that the reason for this is the use of F#'s optimized closures, whose effect we explore in the following.


## The Problem ##

Our data structure builds a two-dimensional binary tree over an ```Array2D``` instance and allows hence for fast concatenation and dynamic parallelism. Comparing the benchmark results for sequential and parallel `init` functions, it showed that parallel  `init` is a whooping four times slower than the sequential variant.

The main difference was that the sequential variant would first initialize the entire underlying array using `Array2D.init` and then construct a tree structure above it. The parallel variant would initialize the array via `Array2D.zeroCreate`, then build the tree structure recursively and in parallel and initialize the array's values at the leafs.


## Optimized Closures ##

When digging into the code for `Array2D.init`, the only difference one can find is that the initialization function `f` is "adapted" by calling `OptimizedClosures.FSharFunc<_,_,_>.Adapt f` [before entering the loop](https://github.com/fsharp/fsharp/blob/master/src/fsharp/FSharp.Core/array2.fs#L76). The [documentation for optimized closures](https://msdn.microsoft.com/en-us/visualfsharpdocs/conceptual/optimizedclosures.fsharpfunc%5B't1,'t2,'u%5D-class-%5Bfsharp%5D) simply says:

> The .NET Framework type used to represent F# function values that
> accept two iterated (curried) arguments without intervening
> execution. This type should not typically used directly from either
> F# code or from other .NET Framework languages.

Furthermore, it says about the `Adapt` function:

> Adapt an F# first class function value to be an optimized function
> value that can accept two curried arguments without intervening
> execution.

It turns out that the `Adapt` function in the [F# core library](https://github.com/fsharp/fsharp/blob/master/src/fsharp/FSharp.Core/prim-types.fs#L2806) really only overrides the `Invoke` method on `FSharpFunc`.

## Benchmarking ##

Intrigued by this finding, we composed some benchmarks. We use a [variant of the LambdaMicrobenchmarks library](https://github.com/fbie/LambdaMicrobenchmarking) which automatically increases the iteration count to produce reliable results.

The baseline is a simple tail-recursive reduce function over cons lists:

```fsharp
let rec reduceStd f e = function
    | [] -> e
    | x :: xs -> reduceStd f (f e x) xs
```

The optimized variant of reduce simply calls `FSharpFunc<_, _, _>.Adapt` and passes the result to the recursive function. The resulting function is invoked via its member function `Invoke()`:

```fsharp
open OptimizedClosures

let reduceOpt f e xs =
    let rec red (f : FSharpFunc<_, _, _>) e = function
        | [] -> e
        | x :: xs -> red f (f.Invoke(e, x)) xs

    // Adapt before calling.
    red (FSharpFunc<_, _, _>.Adapt f) e xs
```

We use these two functions to implement two variants of `sum`:

```fsharp
let sum = reduceStd (+) 0
let sumOpt = reduceOpt (+) 0

```

We run our experiments with the following command:

```
fsi --tailcalls+ --optimize+ ClosuresExperiments.fsx -- n
```

Here, the trailing `n` is the length of the list that we want to compute the sum of.


## Results ##

On .Net 4.6 the effect is drastic. The reduce function that calls `FSharpFunc<_,_,_>.Adapt` is consistently an order of magnitude faster:

```
> run 10
Benchmark                             Mean Mean-Error   Sdev  Unit  Count
sum                               0,000229      0,000  0,000 ms/op 209715
sumOpt                            0,000036      0,000  0,000 ms/op 838860

> run 100
Benchmark                             Mean Mean-Error   Sdev  Unit  Count
sum                               0,002127      0,000  0,000 ms/op 131072
sumOpt                            0,000220      0,000  0,000 ms/op 209715

> run 1000
Benchmark                             Mean Mean-Error   Sdev  Unit  Count
sum                               0,021687      0,000  0,000 ms/op  16384
sumOpt                            0,001948      0,000  0,000 ms/op 131072

> run 10000
Benchmark                             Mean Mean-Error   Sdev  Unit  Count
sum                               0,216141      0,002  0,002 ms/op   2048
sumOpt                            0,019465      0,001  0,000 ms/op  16384
```

On Mono 4.8, the speedup is still two-fold:

```
$ ./run 10
Benchmark                	      Mean Mean-Error   Sdev  Unit  Count
sum                      	  0.000083      0.000  0.000 ms/op 4194304
sumOpt                   	  0.000043      0.000  0.000 ms/op 8388608

$ ./run 100
Benchmark                	      Mean Mean-Error   Sdev  Unit  Count
sum                      	  0.000559      0.000  0.000 ms/op 524288
sumOpt                   	  0.000256      0.000  0.000 ms/op 1048576

$ ./run 1000
Benchmark                	      Mean Mean-Error   Sdev  Unit  Count
sum                      	  0.005334      0.000  0.000 ms/op  65536
sumOpt                   	  0.002414      0.000  0.000 ms/op 131072

$ ./run 10000
Benchmark                	      Mean Mean-Error   Sdev  Unit  Count
sum                      	  0.054457      0.001  0.001 ms/op   8192
sumOpt                   	  0.026524      0.000  0.000 ms/op  16384
```

## Conclusion ##

When implementing data structures, make sure to give them a proper finish by using optimized closures under the hood. The performance benefit is obvious. Clearly, the code gets slightly more convoluted. This should not hinder library implementers from making extensive use of such improvements.

## Further Resources ##

[Jomo Fisher, 2008. "F# Performance Tweaking".](https://blogs.msdn.microsoft.com/jomo_fisher/2008/09/16/f-performance-tweaking/)
