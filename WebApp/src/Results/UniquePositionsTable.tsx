import React from "react";
import { PerftSummary } from "./models/PerftSummary";

interface UniquePositionsTableProps {
  summary: PerftSummary;
}
const UniquePositionsTable: React.FC<UniquePositionsTableProps> = ({
  summary,
}) => {
 
  return (
    <>
      <div className="relative bg-gray-100 rounded-lg p-4">
        <span className="text-md font-bold m-2 text-gray-700 w-full block">
          {summary.position_name} estimated unique positions
        </span>
        <div className="overflow-x-auto">
          <table className="min-w-[200px] text-sm text-left rtl:text-right text-gray-500">
            <thead className="text-xs text-gray-700 uppercase">
              <tr>
                <th scope="col" className="px-6 py-3">
                  Depth
                </th>
                <th scope="col" className="px-6 py-3">
                 Unique Position Count
                </th>
              </tr>
            </thead>
            <tbody>
              {summary.unique_positions.map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">{index}</td>
                  <td className="px-6 py-4">{item.toLocaleString()}</td>      
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default UniquePositionsTable;
