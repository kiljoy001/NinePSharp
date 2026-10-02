using NinePSharp.Constants;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Authorization;
using NinePSharp.Namespaces.Authorization.Tests.Support;

namespace NinePSharp.Fuzzer;

/// <summary>
/// Compares the authorization layer with an independent model of the rules, transcribed from
/// 9front gefs fs.c (fsaccess, ingroup, mode2bits, fscreate, fsremove) and hjfs fs2.c (ORCLOSE),
/// on generated policies, modes, principals and operations. Disagreement or a denied operation
/// reaching the provider fails the run.
/// </summary>
public static class AuthorizationFuzz
{
    private static readonly string[] Paths = ["/", "/data", "/data/report", "/data/private", "/data/tools", "/archive", "/archive/old"];
    private static readonly string[] Files = ["/data/report", "/data/private", "/data/tools", "/archive/old"];
    private static readonly string[] Directories = ["/", "/data", "/archive"];
    private static readonly string[] Users = ["glenda", "alice", "bob", "writers", "none", "mallory"];
    private static readonly byte[] OpenModes =
    [
        NinePConstants.OREAD, NinePConstants.OWRITE, NinePConstants.ORDWR, NinePConstants.OEXEC,
        NinePConstants.OREAD | NinePConstants.OTRUNC, NinePConstants.OREAD | NinePConstants.ORCLOSE,
        NinePConstants.OWRITE | NinePConstants.OTRUNC,
    ];

    private enum Outcome
    {
        Allowed,
        Denied,
        NotFound,
        CreateRejected,
    }

    /// <summary>AFL entry: each case consumes bytes until the input is exhausted.</summary>
    public static void Run(Stream input)
    {
        byte[] buffer = new byte[4096];
        int length = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        Run(buffer.AsSpan(0, length));
    }

    /// <summary>Runs independent cases drawn from the bytes, at most 32.</summary>
    public static void Run(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes[..Math.Min(bytes.Length, 4096)].ToArray());
        for (int count = 0; count < 32 && (count == 0 || !cursor.End); count++)
        {
            RunCase(cursor.Next);
        }
    }

    /// <summary>One generated world and operation; <paramref name="choose"/> returns a value below its bound.</summary>
    public static void RunCase(Func<int, int> choose)
    {
        var world = World.Generate(choose);
        string user = Users[choose(Users.Length)];
        int operation = choose(4);
        string file = Files[choose(Files.Length)];
        string directory = Directories[choose(Directories.Length)];
        byte mode = OpenModes[choose(OpenModes.Length)];

        Outcome expected = operation switch
        {
            0 => world.ExpectOpen(user, file, mode),
            1 => world.ExpectStat(user, file),
            2 => world.ExpectRemove(user, file),
            _ => world.ExpectCreate(user, directory, mode & 3),
        };

        string kind = operation switch { 0 => "open", 1 => "stat", 2 => "remove", _ => "create" };
        string target = operation == 3 ? directory : file;
        int before = world.Tree.CallsMatching($"{kind} {target}");
        Outcome actual = world.Run(user, operation, file, directory, mode);
        Check(expected == actual, $"{kind} {target} as {user}: model {expected}, layer {actual}");
        Check(
            world.Tree.CallsMatching($"{kind} {target}") == before + (actual == Outcome.Allowed ? 1 : 0),
            "a denied operation reached the provider");
    }

    private static void Check(bool condition, string invariant)
    {
        if (!condition)
        {
            throw new InvalidOperationException(invariant);
        }
    }

    private sealed class Cursor(byte[] bytes)
    {
        private int position;

        internal bool End => position >= bytes.Length;

        internal int Next(int bound) => (position < bytes.Length ? bytes[position++] : 0) % bound;
    }

    private sealed class World
    {
        private readonly Dictionary<string, (string Owner, string Group, uint Mode)> nodes = new(StringComparer.Ordinal);
        private readonly HashSet<string> disabled = new(StringComparer.Ordinal);
        private readonly HashSet<(string Group, string User)> members = new();
        private readonly List<(bool Group, string Subject, string Path, bool Tree, ResourceRights Rights)> grants = new();
        private readonly HashSet<string> readOnly = new(StringComparer.Ordinal);
        private bool stale;

        internal TreeResources Tree { get; } = new();

        internal static World Generate(Func<int, int> choose)
        {
            var world = new World();
            foreach (string path in Paths)
            {
                bool directory = Directories.Contains(path);
                string owner = choose(6) switch { 0 => "alice", 1 => "none", 2 => "writers", _ => "glenda" };
                string group = choose(2) == 0 ? "writers" : "sys";
                uint mode = (uint)choose(512);
                world.nodes[path] = (owner, group, mode);
                world.Tree.Add(path, directory, owner, group, mode | (directory ? (uint)NinePConstants.FileMode9P.DMDIR : 0));
            }

            world.disabled.Add("mallory");
            world.members.Add(("writers", "alice"));
            if (choose(2) == 0)
            {
                world.members.Add(("writers", "bob"));
            }

            if (choose(3) == 0)
            {
                world.members.Add(("nogroup", Users[choose(Users.Length)]));
            }

            int count = choose(6);
            for (int index = 0; index < count; index++)
            {
                bool group = choose(3) == 0;
                string subject = group ? "writers" : Users[choose(Users.Length)];
                string path = Paths[choose(Paths.Length)];
                bool tree = choose(2) == 0;
                var rights = (ResourceRights)(1 + choose(63));
                if (world.grants.Any(grant => grant == (group, subject, path, tree, rights)))
                {
                    continue;
                }

                world.grants.Add((group, subject, path, tree, rights));
            }

            if (choose(4) == 0)
            {
                world.readOnly.Add(choose(2) == 0 ? "/data" : "/archive");
            }

            world.stale = choose(8) == 0;
            return world;
        }

        internal Outcome Run(string user, int operation, string file, string directory, byte mode)
        {
            var policy = new AuthorizationPolicy(
                1,
                Users.Select(name => new AuthorizationPrincipal(name, !disabled.Contains(name))),
                members.Select(pair => new GroupMembership(pair.Group, pair.User)),
                grants.Select(grant => new ResourceGrant(
                    grant.Group ? GrantSubjectKind.Group : GrantSubjectKind.User,
                    grant.Subject,
                    Tree.Handle(grant.Path).Identity,
                    grant.Tree ? GrantScope.Tree : GrantScope.Self,
                    grant.Rights)));
            var view = new AuthorizedResourceOperations(
                Tree,
                Tree,
                policy,
                user,
                () => stale ? 2UL : 1UL,
                readOnly.Select(path => Tree.Handle(path).Identity));
            var context = new ResourceOperationContext(new ResourceOperationId("oracle", 1), 1, user);
            try
            {
                switch (operation)
                {
                    case 0:
                        _ = view.OpenAsync(Tree.Handle(file), mode, context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                        break;
                    case 1:
                        _ = view.StatAsync(Tree.Handle(file), CancellationToken.None).AsTask().GetAwaiter().GetResult();
                        break;
                    case 2:
                        view.RemoveAsync(Tree.Handle(file), null, context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                        break;
                    default:
                        _ = view.CreateAndOpenAsync(Tree.Handle(directory), "new", 0x1B6, (byte)(mode & 3), context, CancellationToken.None)
                            .AsTask().GetAwaiter().GetResult();
                        break;
                }

                return Outcome.Allowed;
            }
            catch (ResourceAccessDeniedException)
            {
                return Outcome.Denied;
            }
            catch (ResourceCreateRejectedException)
            {
                return Outcome.CreateRejected;
            }
            catch (NamespaceException error) when (error.Error == NamespaceError.ResourceNotFound)
            {
                return Outcome.NotFound;
            }
        }

        internal Outcome ExpectOpen(string user, string path, byte mode)
        {
            if (!Current(user))
            {
                return Outcome.Denied;
            }

            ResourceRights held = Rights(user, path);
            if (held == ResourceRights.None)
            {
                return Outcome.NotFound;
            }

            (ResourceRights needed, uint bits) = (mode & 3) switch
            {
                0 => (ResourceRights.Read, 4U),
                1 => (ResourceRights.Write, 2U),
                2 => (ResourceRights.Read | ResourceRights.Write, 6U),
                _ => (ResourceRights.Read, 5U),
            };
            bool mutates = (mode & 3) is 1 or 2;
            if ((mode & NinePConstants.OTRUNC) != 0)
            {
                needed |= ResourceRights.Write;
                bits |= 2;
                mutates = true;
            }

            if ((mode & NinePConstants.ORCLOSE) != 0)
            {
                needed |= ResourceRights.Remove;
                mutates = true;
                if (!Permits(user, Parent(path), 2))
                {
                    return Outcome.Denied;
                }
            }

            if ((held & needed) != needed || !Permits(user, path, bits))
            {
                return Outcome.Denied;
            }

            return mutates && ReadOnly(path) ? Outcome.Denied : Outcome.Allowed;
        }

        internal Outcome ExpectStat(string user, string path)
        {
            if (!Current(user))
            {
                return Outcome.Denied;
            }

            ResourceRights held = Rights(user, path);
            if (held == ResourceRights.None)
            {
                return Outcome.NotFound;
            }

            return (held & ResourceRights.Stat) != 0 ? Outcome.Allowed : Outcome.Denied;
        }

        internal Outcome ExpectRemove(string user, string path)
        {
            if (!Current(user))
            {
                return Outcome.Denied;
            }

            ResourceRights held = Rights(user, path);
            if (held == ResourceRights.None)
            {
                return Outcome.NotFound;
            }

            if ((held & ResourceRights.Remove) == 0 || !Permits(user, Parent(path), 2) || ReadOnly(path))
            {
                return Outcome.Denied;
            }

            return Outcome.Allowed;
        }

        internal Outcome ExpectCreate(string user, string directory, int baseMode)
        {
            if (!Current(user))
            {
                return Outcome.CreateRejected;
            }

            ResourceRights needed = baseMode switch
            {
                0 => ResourceRights.Read,
                1 => ResourceRights.Write,
                2 => ResourceRights.Read | ResourceRights.Write,
                _ => ResourceRights.Read,
            };
            ResourceRights inherited = Inherited(user, directory);
            bool allowed = (Rights(user, directory) & ResourceRights.Create) != 0 && Permits(user, directory, 2) &&
                (inherited & needed) == needed && !ReadOnly(directory);
            return allowed ? Outcome.Allowed : Outcome.CreateRejected;
        }

        private static bool IsAncestor(string ancestor, string path)
            => path != ancestor && (ancestor == "/" || path.StartsWith(ancestor + "/", StringComparison.Ordinal));

        private static string Parent(string path) => path.LastIndexOf('/') == 0 ? "/" : path[..path.LastIndexOf('/')];

        private bool Current(string user) => !stale && !disabled.Contains(user);

        private ResourceRights Rights(string user, string path)
            => grants.Where(grant => Subject(user, grant.Group, grant.Subject) &&
                    (grant.Path == path || (grant.Tree && IsAncestor(grant.Path, path))))
                .Aggregate(ResourceRights.None, (rights, grant) => rights | grant.Rights);

        private ResourceRights Inherited(string user, string directory)
            => grants.Where(grant => Subject(user, grant.Group, grant.Subject) && grant.Tree &&
                    (grant.Path == directory || IsAncestor(grant.Path, directory)))
                .Aggregate(ResourceRights.None, (rights, grant) => rights | grant.Rights);

        private bool Subject(string user, bool group, string subject) => group ? InGroup(user, subject) : subject == user;

        /// <summary>gefs ingroup: the same-name group, or an explicit member; not recursive.</summary>
        private bool InGroup(string user, string group) => user == group || members.Contains((group, user));

        private bool ReadOnly(string path) => readOnly.Any(root => root == path || IsAncestor(root, path));

        /// <summary>gefs fsaccess: owner set, then group set for members, then the other set.</summary>
        private bool Permits(string user, string path, uint bits)
        {
            (string owner, string group, uint mode) = nodes[path];
            if (user != "none")
            {
                if (owner == user && ((mode >> 6) & bits) == bits)
                {
                    return true;
                }

                if (InGroup(user, group) && ((mode >> 3) & bits) == bits)
                {
                    return true;
                }
            }

            if ((mode & bits) != bits)
            {
                return false;
            }

            if (Directories.Contains(path) && bits == 1)
            {
                return true;
            }

            return !InGroup(user, "nogroup");
        }
    }
}
