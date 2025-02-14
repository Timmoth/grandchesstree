import React from "react";
import { PerftSummary } from "./models/PerftSummary";

interface PerftResultsTableProps {
  summary: PerftSummary,
}
const PerftResultsTable: React.FC<PerftResultsTableProps> = ({ summary  }) => {
  

  return (
    <>
      <div className="relative w-full bg-gray-100 rounded-lg p-4">
    
        <span className="text-md font-bold m-2 text-gray-700 w-full block">
        {summary.position_name} results table
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
                  Captures
                </th>
                <th scope="col" className="px-6 py-3">
                  Enpassants
                </th>
                <th scope="col" className="px-6 py-3">
                  Castles
                </th>
                <th scope="col" className="px-6 py-3">
                  Promotions
                </th>
                <th scope="col" className="px-6 py-3">
                  Direct Checks
                </th>
                <th scope="col" className="px-6 py-3">
                  Single Discovered Checks
                </th>
                <th scope="col" className="px-6 py-3">
                  Direct Discovered Checks
                </th>
                <th scope="col" className="px-6 py-3">
                  Double Discovered Check
                </th>
                <th scope="col" className="px-6 py-3">
                  Total Checks
                </th>
                <th scope="col" className="px-6 py-3">
                  Direct Mates
                </th>
                <th scope="col" className="px-6 py-3">
                  Single Discovered Mates
                </th>
                <th scope="col" className="px-6 py-3">
                  Direct Discovered Mates
                </th>
                <th scope="col" className="px-6 py-3">
                  Double Discovered Mates
                </th>
                <th scope="col" className="px-6 py-3">
                  Total Mates
                </th>
              </tr>
            </thead>
            <tbody>
              {summary.results.map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">{item.depth.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.nodes.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.captures.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.enpassants.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.castles.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.promotions.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.direct_checks.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.single_discovered_checks.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.direct_discovered_checks.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.double_discovered_checks.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.total_checks.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.direct_mates.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.single_discovered_mates.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.direct_discovered_mates.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.double_discovered_mates.toLocaleString()}</td>
                  <td className="px-6 py-4">{item.total_mates.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default PerftResultsTable;
