import React from "react";

interface PerftData {
  depth: number;
  nodes: number;
  captures: number;
  enpassants: number;
  castles: number;
  promotions: number;
  direct_checks: number;
  single_discovered_checks: number;
  direct_discovered_checks: number;
  double_discovered_check: number;
  total_checks: number;
  direct_mates: number;
  single_discovered_mates: number;
  direct_discovered_mates: number;
  double_discovered_mates: number;
  total_mates: number;
}

// Hardcoded data array
const leaderboardData: PerftData[] = [
  {
    depth: 0,
    nodes: 1,
    captures: 0,
    enpassants: 0,
    castles: 0,
    promotions: 0,
    direct_checks: 0,
    single_discovered_checks: 0,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 0,
    direct_mates: 0,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 0,
  },
  {
    depth: 1,
    nodes: 20,
    captures: 0,
    enpassants: 0,
    castles: 0,
    promotions: 0,
    direct_checks: 0,
    single_discovered_checks: 0,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 0,
    direct_mates: 0,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 0,
  },
  {
    depth: 2,
    nodes: 400,
    captures: 0,
    enpassants: 0,
    castles: 0,
    promotions: 0,
    direct_checks: 0,
    single_discovered_checks: 0,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 0,
    direct_mates: 0,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 0,
  },
  {
    depth: 3,
    nodes: 8902,
    captures: 34,
    enpassants: 0,
    castles: 0,
    promotions: 0,
    direct_checks: 12,
    single_discovered_checks: 0,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 12,
    direct_mates: 0,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 0,
  },
  {
    depth: 4,
    nodes: 197281,
    captures: 1576,
    enpassants: 0,
    castles: 0,
    promotions: 0,
    direct_checks: 461,
    single_discovered_checks: 0,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 461,
    direct_mates: 8,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 8,
  },
  {
    depth: 5,
    nodes: 4865609,
    captures: 82719,
    enpassants: 258,
    castles: 0,
    promotions: 0,
    direct_checks: 26998,
    single_discovered_checks: 6,
    direct_discovered_checks: 0,
    double_discovered_check: 0,
    total_checks: 27004,
    direct_mates: 347,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 347,
  },
  {
    depth: 6,
    nodes: 119060324,
    captures: 2812008,
    enpassants: 5248,
    castles: 0,
    promotions: 0,
    direct_checks: 797896,
    single_discovered_checks: 329,
    direct_discovered_checks: 46,
    double_discovered_check: 0,
    total_checks: 798271,
    direct_mates: 10828,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 10828,
  },
  {
    depth: 7,
    nodes: 3195901860,
    captures: 108329926,
    enpassants: 319617,
    castles: 883453,
    promotions: 0,
    direct_checks: 32648427,
    single_discovered_checks: 18026,
    direct_discovered_checks: 1628,
    double_discovered_check: 0,
    total_checks: 32668081,
    direct_mates: 435767,
    single_discovered_mates: 0,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 435767,
  },
  {
    depth: 8,
    nodes: 84998978956,
    captures: 3523740106,
    enpassants: 7187977,
    castles: 23605205,
    promotions: 0,
    direct_checks: 958135303,
    single_discovered_checks: 847039,
    direct_discovered_checks: 147215,
    double_discovered_check: 0,
    total_checks: 959129557,
    direct_mates: 9852032,
    single_discovered_mates: 4,
    direct_discovered_mates: 0,
    double_discovered_mates: 0,
    total_mates: 9852036,
  },
  {
    depth: 9,
    nodes: 2439530234167,
    captures: 125208536153,
    enpassants: 319496827,
    castles: 1784356000,
    promotions: 17334376,
    direct_checks: 35653060996,
    single_discovered_checks: 37101713,
    direct_discovered_checks: 5547221,
    double_discovered_check: 10,
    total_checks: 35695709940,
    direct_mates: 399421379,
    single_discovered_mates: 1869,
    direct_discovered_mates: 768715,
    double_discovered_mates: 0,
    total_mates: 400191963,
  },
  {
    depth: 10,
    nodes: 69352859712417,
    captures: 4092784875884,
    enpassants: 7824835694,
    castles: 50908510199,
    promotions: 511374376,
    direct_checks: 1077020493859,
    single_discovered_checks: 1531274015,
    direct_discovered_checks: 302900733,
    double_discovered_check: 879,
    total_checks: 1078854669486,
    direct_mates: 8771693969,
    single_discovered_mates: 598058,
    direct_discovered_mates: 18327128,
    double_discovered_mates: 0,
    total_mates: 8790619155,
  },
  {
    depth: 11,
    nodes: 2097651003696806,
    captures: 142537161824567,
    enpassants: 313603617408,
    castles: 2641343463566,
    promotions: 49560932860,
    direct_checks: 39068470901662,
    single_discovered_checks: 67494850305,
    direct_discovered_checks: 11721852393,
    double_discovered_check: 57443,
    total_checks: 39147687661803,
    direct_mates: 360675926605,
    single_discovered_mates: 60344676,
    direct_discovered_mates: 1553739626,
    double_discovered_mates: 0,
    total_mates: 362290010907,
  },
];

const PerftResults: React.FC = () => {
  return (
    <>
      <div className="relative w-full bg-gray-100 rounded-lg p-4">
        <span className="text-md font-bold m-2 text-gray-700 w-full block">
          Perft results
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
              {leaderboardData.map((item, index) => (
                <tr key={index} className="bg-white border-b border-gray-200">
                  <td className="px-6 py-4">{item.depth}</td>
                  <td className="px-6 py-4">{item.nodes}</td>
                  <td className="px-6 py-4">{item.captures}</td>
                  <td className="px-6 py-4">{item.enpassants}</td>
                  <td className="px-6 py-4">{item.castles}</td>
                  <td className="px-6 py-4">{item.promotions}</td>
                  <td className="px-6 py-4">{item.direct_checks}</td>
                  <td className="px-6 py-4">{item.single_discovered_checks}</td>
                  <td className="px-6 py-4">{item.direct_discovered_checks}</td>
                  <td className="px-6 py-4">{item.double_discovered_check}</td>
                  <td className="px-6 py-4">{item.total_checks}</td>
                  <td className="px-6 py-4">{item.direct_mates}</td>
                  <td className="px-6 py-4">{item.single_discovered_mates}</td>
                  <td className="px-6 py-4">{item.direct_discovered_mates}</td>
                  <td className="px-6 py-4">{item.double_discovered_mates}</td>
                  <td className="px-6 py-4">{item.total_mates}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};

export default PerftResults;
