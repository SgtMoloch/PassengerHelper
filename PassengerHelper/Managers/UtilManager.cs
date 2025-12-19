using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using PassengerHelper.UMM;

namespace PassengerHelper.Managers;

public class UtilManager
{
    internal readonly List<string> orderedStations = new List<string>()
                {
                "sylva", "dillsboro", "wilmot", "whittier", "ela", "bryson", "hemingway", "alarkajct", "cochran", "alarka",
                "almond", "nantahala", "topton", "rhodo", "andrews"
                };

    public List<PassengerStop> GetPassengerStops()
    {
        // List<PassengerStop> passStops = PassengerStop.FindAll().ToList();

        // Dictionary<string, List<string>> adjacencyList = new Dictionary<string, List<string>>();

        // Graph<string> graph = new Graph<string>();

        // passStops.ForEach(ps =>
        // {
        //     PassengerStop[] neighbors = ps.neighbors;

        //     foreach (PassengerStop n in neighbors)
        //     {
        //         graph.AddEdge(ps.identifier, n.identifier, false);
        //     }

        // });

        // List<PassengerStop> passStopsOrdered = graph.TraverseBranchFirst(passStops.Where(ps => ps.identifier == "sylva").First(), new List<string>() { "alarkajct", "cochran", "alarka" });

        // passStopsOrdered.ForEach(ps => Loader.Log(ps.identifier + ", "));


        // foreach (PassengerStop ps in passStops)
        // {
        //     PassengerStop[] neighbors = ps.neighbors;

        //     string[] neighborNames = ps.neighbors.Select(ps => ps.DisplayName).ToArray();

        //     Loader.Log($"Passenger stop: {ps.DisplayName} has the following neighbors: {neighborNames}");
        //     Loader.Log($"Acutal order of neighbors in array:");
        //     foreach (PassengerStop n in neighbors)
        //     {
        //         Loader.Log($"{n.DisplayName}");
        //     }
        // }

        return PassengerStop.FindAll()
        .Where(ps => !ps.ProgressionDisabled && orderedStations.Contains(ps.identifier))
        .OrderBy(d => orderedStations.IndexOf(d.identifier))
        .ToList();
    }
}

class Graph<T>
{
    // A dictionary where the key is a node and the value is a list of its neighbors (adjacency list)
    private Dictionary<T, List<T>> adjacencyList = new Dictionary<T, List<T>>();

    // Method to add a new node (vertex) to the graph
    public void AddNode(T node)
    {
        if (!adjacencyList.ContainsKey(node))
        {
            adjacencyList[node] = new List<T>();
        }
    }

    // Method to add an edge between two nodes
    // The 'undirected' parameter makes the edge bidirectional
    public void AddEdge(T fromNode, T toNode, bool undirected = true)
    {
        // Ensure both nodes exist
        AddNode(fromNode);
        AddNode(toNode);

        // Add the edge from fromNode to toNode
        adjacencyList[fromNode].Add(toNode);

        // If undirected, add the reverse edge
        if (undirected && !fromNode.Equals(toNode))
        {
            adjacencyList[toNode].Add(fromNode);
        }
    }

    // Method to display the graph (for demonstration)
    public void DisplayGraph()
    {
        Loader.Log("Graph Adjacency List:");
        foreach (var entry in adjacencyList)
        {
            Loader.Log($"{entry.Key}: ");
            Loader.Log(string.Join(", ", entry.Value));
        }
    }

    public void Walk(T startNode)
    {
        var visited = new HashSet<T>();
        var queue = new Queue<T>();

        queue.Enqueue(startNode);
        visited.Add(startNode);

        while (queue.Count > 0)
        {
            T currentNode = queue.Dequeue();
            Loader.Log($"Visited: {currentNode}");

            foreach (T neighbor in adjacencyList[currentNode])
            {
                Loader.Log($"checking {neighbor}");
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    public List<PassengerStop> TraverseBranchFirst(
        PassengerStop start,
        IEnumerable<string> branchIdentifiers)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));

        var branchIds = new HashSet<string>(branchIdentifiers ?? Array.Empty<string>());

        // Reference-equality visited set (don’t rely on PassengerStop.Equals)
        var visited = new HashSet<PassengerStop>(ReferenceEqualityComparer<PassengerStop>.Instance);

        // (current, parent) avoids immediately walking back on bidirectional links
        var stack = new Stack<(PassengerStop cur, PassengerStop? parent)>();
        var ordered = new List<PassengerStop>();

        stack.Push((start, parent: null));

        while (stack.Count > 0)
        {
            var (cur, parent) = stack.Pop();
            if (!visited.Add(cur))
                continue;

            ordered.Add(cur);

            var mainline = new List<PassengerStop>();
            var branch = new List<PassengerStop>();

            var next = cur.neighbors;
            if (next != null)
            {
                foreach (var n in next)
                {
                    if (n == null) continue;
                    if (ReferenceEquals(n, parent)) continue;

                    if (n.identifier != null && branchIds.Contains(n.identifier))
                        branch.Add(n);
                    else
                        mainline.Add(n);
                }
            }

            // Stack ordering: push mainline first, then branch => branch explored first
            for (int i = mainline.Count - 1; i >= 0; i--)
                stack.Push((mainline[i], cur));

            for (int i = branch.Count - 1; i >= 0; i--)
                stack.Push((branch[i], cur));
        }

        return ordered;
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}