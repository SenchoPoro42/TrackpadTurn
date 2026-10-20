import os

/// Unified logging. Watch live with:
///   log stream --level debug --predicate 'subsystem == "io.kartin.TrackpadTurn"'
enum Log {
    /// Per-event detail (debug level: free unless someone is streaming it) and swipe summaries.
    static let input = Logger(subsystem: "io.kartin.TrackpadTurn", category: "input")
    /// Permission and event tap lifecycle.
    static let access = Logger(subsystem: "io.kartin.TrackpadTurn", category: "access")
}
