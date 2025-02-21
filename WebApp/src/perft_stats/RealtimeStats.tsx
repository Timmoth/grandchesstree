import React, { useEffect, useState } from "react";

// Type definition for the leaderboard response
interface PerftStatsResponse {
  nps: number;
  tpm: number;
  completed_tasks: number;
  total_nodes: number;
  percent_completed_tasks: number;
  total_tasks: number;
}
interface RealtimeStatsProps {
  positionId: number,
  depth: number,
}
const RealtimeStats: React.FC<RealtimeStatsProps> = ({ positionId, depth }) => {
  const [leaderboardData, setLeaderboardData] = useState<PerftStatsResponse>();
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Fetch the leaderboard data from the API
    const fetchLeaderboard = async () => {
      try {
        const resp = await fetch(
          `https://api.grandchesstree.com/api/v2/perft/${positionId}/${depth}/stats`
        );
        if (!resp.ok) {
          throw new Error("Failed to fetch data");
        }
        const data: PerftStatsResponse = await resp.json(); // Explicitly typing the response
        setLeaderboardData(data); // Store the data in state
      } catch (err: any) {
        setError(err.message); // Store error message if there's an issue with the fetch
      } finally {
        setLoading(false); // Set loading to false once data is fetched
      }
    };

    fetchLeaderboard();
  }, [positionId, depth]); // Empty dependency array ensures this effect runs only once when the component mounts

  // Format large numbers (e.g., 1000 -> 1k, 1000000 -> 1m)
  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString(); // Return as is if it's less than 1000
  };

  const formatTime = (seconds: number): string => {
    if (seconds < 60) {
      return `${seconds}s`; // Less than 1 minute
    } else if (seconds < 3600) {
      const minutes = Math.floor(seconds / 60);
      const remainingSeconds = seconds % 60;
      return `${minutes}m ${remainingSeconds}s`; // Less than 1 hour
    } else if (seconds < 86400) {
      const hours = Math.floor(seconds / 3600);
      const remainingMinutes = Math.floor((seconds % 3600) / 60);
      return `${hours}h ${remainingMinutes}m`; // Less than 1 day
    } else {
      const days = Math.floor(seconds / 86400);
      return `${days}d`; // More than 1 day
    }
  };

  if (loading) {
    return <p>Loading...</p>;
  }

  if (error) {
    return <p>Error: {error}</p>;
  }

  const completed = leaderboardData?.completed_tasks == 101240;

  return (
    <>
      <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700">
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Perft Depth</span>
          <span className="text-xl font-bold">{depth}</span>
        </div>

        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Nodes / Second</span>
          <span className="text-xl font-bold">
            {leaderboardData && formatBigNumber(leaderboardData?.nps)}
          </span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Tasks / Minute</span>
          <span className="text-xl font-bold">
            {leaderboardData && formatBigNumber(leaderboardData?.tpm)}
          </span>
        </div>

        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Completed Tasks</span>
          <span className="text-xl font-bold">
            {leaderboardData && leaderboardData?.completed_tasks} / {leaderboardData?.total_tasks}
          </span>
        </div>

        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Total Nodes</span>
          <span className="text-xl font-bold">
            {leaderboardData && formatBigNumber(leaderboardData?.total_nodes)}
          </span>
        </div>
        <div className="flex justify-between items-center space-x-4">
          <span className="text-md font-semibold">Time Remaining</span>
          <span className="text-xl font-bold">
            {completed
              ? "Completed"
              : leaderboardData &&
                formatTime(
                  ((leaderboardData?.total_tasks - leaderboardData?.completed_tasks) /
                    leaderboardData?.tpm) *
                    60
                )}
          </span>
        </div>

        <div className="flex justify-between items-center space-x-4">
          <div
            className="bg-blue-600 text-xs font-medium text-blue-100 text-center p-0.5 leading-none rounded-full"
            style={{ width: `${leaderboardData?.percent_completed_tasks}%` }}
          >
            {leaderboardData &&
              Math.round(leaderboardData?.percent_completed_tasks)}
            %
          </div>
        </div>
      </div>
    </>
  );
};

export default RealtimeStats;
