﻿namespace Graphs

open System.Collections.Generic
open System.Linq
open System.IO
open System
open DrawGraph
open System.Linq
open Alea.CUDA

#nowarn "25"

[<AutoOpen>]
module Extensions =
    type Graphs.DirectedGraph<'a when 'a:comparison> with
        /// <summary>
        /// Generates a Eulerian graph
        /// </summary>
        /// <param name="n"> number of vertices </param>
        /// <param name="k"> max number of connections in one direction</param>
        static member GenerateEulerGraph(n, k, ?path) =
            let isPath = defaultArg path false

            let rnd = Random(int DateTime.Now.Ticks)
            let connections = Array.init n (fun i -> rnd.Next(1, k + 1))
            let connectionsReverse = Array.zeroCreate n
            connections.CopyTo(connectionsReverse, 0)

            if isPath then
                let outLessThanIn = rnd.Next(n)
                let mutable inLessThanOut = rnd.Next(n)
                while outLessThanIn = inLessThanOut do
                    inLessThanOut <- rnd.Next(n)
                connections.[outLessThanIn] <- connections.[outLessThanIn] - 1
                connectionsReverse.[inLessThanOut] <- connections.[inLessThanOut] - 1

            let rowIndex = [0].ToList()
            let colIndex = List<int>()
            for i = 0 to n - 1 do
                rowIndex.Add(0)
                rowIndex.[i + 1] <- rowIndex.[i] + connections.[i]

                // scan vertices starting from vertex i and grab the next available vertex to connect to while it is possible
                // connectionsReverse keeps track of each vertex ability to serve as an inbound vertex. At the end, all
                // of its elements should be eq to 0
                let cols =
                    (0, 1)
                    |> Seq.unfold
                        (fun (st, k) ->
                            let idx = (i + k) % n
                            if st = connections.[i] then None
                            elif connectionsReverse.[idx] = 0
                            then Some(-1, (st, k + 1))
                            else
                                connectionsReverse.[idx] <- connectionsReverse.[idx] - 1
                                Some(idx, (st + 1, k + 1)))
                    |> Seq.filter(fun x -> x >= 0)
                colIndex.AddRange cols
            DirectedGraph(rowIndex, colIndex, ([0..rowIndex.Count - 2].ToDictionary((fun s -> s.ToString()), id) :> IDictionary<string, int>))



        /// <summary>
        /// Create the graph from an array of strings
        /// </summary>
        /// <param name="lines">
        /// array of strings formatted: out_vertex -> in_v1, in_v2, in_v3,..
        ///</param>
        static member FromStrings (lines : string seq) =

            let rowIndexRaw = List<int>()
            let colIndex = List<int>()

            let nameToOrdinal = Dictionary<string, int>() // vertices and the index to which they correspond

            let addVertex (line : string) =

                let vertex, connected =
                    if line.Contains("->") then
                        line.Trim().Split([|"->"|], 2, StringSplitOptions.RemoveEmptyEntries) |> fun [|a; b|] -> a.Trim(), b.Trim()
                    else line.Trim(), ""

                let newVertex = not (nameToOrdinal.ContainsKey vertex)
                if newVertex then
                    nameToOrdinal.Add(vertex, nameToOrdinal.Keys.Count)
                    rowIndexRaw.Add 0

                // get vertices connected to this one
                let connectedVertices =
                    if not (String.IsNullOrEmpty connected) then
                        connected.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun e -> e.Trim())
                    else [||]

                let ordinal = nameToOrdinal.[vertex]

                // store all the vertices we have not seen before
                let newVertices = connectedVertices.Except nameToOrdinal.Keys |> Seq.toArray
                newVertices
                |> Seq.iter (fun v -> nameToOrdinal.Add(v, nameToOrdinal.Keys.Count))

                // extend the new rows
                let newVerticesZeroes = Array.zeroCreate newVertices.Length
                rowIndexRaw.AddRange newVerticesZeroes

                // for now we will store the number of vertices in the row index
                // entry for the given row. We will need to scan it and update its values later
                rowIndexRaw.[nameToOrdinal.[vertex]] <- connectedVertices.Length

                let connectedOrdinals =
                    connectedVertices
                    |> Seq.map (fun v -> nameToOrdinal.[v])

                // if we are inserting a "retoractive" row, we need to know where we are inserting it!
                if newVertex then colIndex.AddRange connectedOrdinals
                else
                    let rowIndexCur = rowIndexRaw |> Seq.scan (+) 0 |> Seq.toList
                    colIndex.InsertRange(rowIndexCur.[ordinal], connectedOrdinals)

            lines |> Seq.iter addVertex

            DirectedGraph<string>(rowIndexRaw |> Seq.scan (+) 0, colIndex, nameToOrdinal)


        /// <summary>
        /// Create the graph from a file
        /// </summary>
        /// <param name="fileName"></param>
        static member FromFile (fileName : string) =

            if String.IsNullOrWhiteSpace fileName || not (File.Exists fileName) then failwith "Invalid file"

            let lines = File.ReadLines(fileName)
            DirectedGraph<string>.FromStrings(lines)

        static member FromInts (ints : int seq) =
            let rowIndex = [0..ints.Count()]
            let nameToOrdinal = rowIndex.[0..rowIndex.Length - 2].ToDictionary((fun v -> v.ToString()), (fun v -> v))
            StrGraph(rowIndex, ints, nameToOrdinal)

        static member SaveStrs ((gr : DirectedGraph<string>), fileName : string) =
            let toVertices (arr : string []) =
                if arr |> Array.isEmpty then String.Empty
                else
                    " -> " +
                    (arr
                    |> Array.reduce  (fun st e -> st + "," + string e))

            let strs =
                gr.AsEnumerable
                |> Seq.map (fun (v, arr) -> v + toVertices arr)
                |> Seq.toArray

            File.WriteAllLines(fileName, strs)