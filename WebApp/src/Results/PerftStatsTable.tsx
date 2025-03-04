import React from "react";
import { PerftSummary } from "./models/PerftSummary";
import FormattedNumber from "../FormattedNumber";
import { Link } from "react-router-dom";

interface PerftStatsTableProps {
  summary: PerftSummary,
}
const PerftStatsTable: React.FC<PerftStatsTableProps> = ({ summary  }) => {
  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString();
  };
  const formatDate = (unixSeconds: number): string => {
    return new Date(unixSeconds * 1000).toLocaleString(undefined, {
      hour: "2-digit",
      minute: "2-digit",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    });
    };

  const formatDuration = (seconds: number): string => {
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = seconds % 60;
  
    if (days > 0) {
      return `${days}d ${hours}h`;
    }else if(hours > 1){
      return `${hours}h ${minutes}m`;
    }else{
      return `${minutes}m ${secs}s`;
    }
  };
  return (
    <>
      <div className="relative w-full bg-gray-100 rounded-lg p-4">
    
        <span className="text-md font-bold m-2 text-gray-700 w-full block">
          {summary.position_name} work table
        </span>
        <div className="w-full overflow-x-auto">
          <table className="min-w-[1000px] text-sm text-left rtl:text-right text-gray-500">
            <thead className="text-xs text-gray-700 uppercase">
              <tr>
                <th scope="col" className="px-6 py-3">
                  Depth
                </th>
                <th scope="col" className="px-6 py-3">
                  Nodes
                </th>
                <th scope="col" className="px-6 py-3">
                  Duration
                </th>
                <th scope="col" className="px-6 py-3">
                  Tasks
                </th>
                <th scope="col" className="px-6 py-3">
                  Contributors
                </th>
                
                <th scope="col" className="px-6 py-3">
                  Started At
                </th>
                <th scope="col" className="px-6 py-3">
                  Finished At
                </th>
     
              </tr>
            </thead>
            <tbody>
            {summary.results.map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">{item.depth.toLocaleString()}</td>
                  <td className="px-6 py-4">{formatBigNumber(parseInt(item.nodes))}</td>
                  <td className="px-6 py-4">{formatDuration(item.finished_at - item.started_at)}</td>
                  <td className="px-6 py-4">{item.total_tasks}</td>
                  <td className="px-6 py-4">
                  {item.contributors
    .sort((a, b) => b.tasks - a.tasks)
    .map((c, index) => (
      <span key={c.id} className="mr-2">
        [
        <Link className="font-medium text-blue-600 hover:underline" to={`/accounts/${c.id}`}>
          {c.name}
        </Link>{" "}
        <FormattedNumber value={c.tasks} min={1e1} max={1e6} />
        ]
        {index !== item.contributors.length - 1 && ", "}
      </span>
    ))}
                  </td>
                  <td className="px-6 py-4">{formatDate(item.started_at)}</td>
                  <td className="px-6 py-4">{formatDate(item.finished_at)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default PerftStatsTable;
