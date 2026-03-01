namespace NinePSharp.Core.FSharp

/// Shared path hashing for stable channel/mount identity
module PathHash =

    /// Normalize and hash a path with FNV-1a for stable Qid/Dev generation
    let stableHash (kind: char) (path: string list) =
        let splitAndNormalize (pathStr: string) =
            pathStr.Split([| '/' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> List.ofArray

        let normalized =
            match splitAndNormalize ("/" + System.String.Join("/", path)) with
            | [] -> "/"
            | segments -> "/" + System.String.Join("/", segments)

        let key = System.String.Concat(kind, ":", normalized)
        let bytes = System.Text.Encoding.UTF8.GetBytes(key)
        let mutable hash = 14695981039346656037UL

        for b in bytes do
            hash <- (hash ^^^ uint64 b) * 1099511628211UL

        hash
