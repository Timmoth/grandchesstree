import { Contributor } from "./Contributor";


export interface ChessData {
  position: number;
  depth: number;
  nodes: string;
  captures: number;
  enpassants: number;
  castles: number;
  promotions: number;
  direct_checks: number;
  single_discovered_checks: number;
  direct_discovered_checks: number;
  double_discovered_checks: number;
  total_checks: number;
  direct_mates: number;
  single_discovered_mates: number;
  direct_discovered_mates: number;
  double_discovered_mates: number;
  total_mates: number;
  total_tasks: number;
  started_at: number;
  finished_at: number;
  contributors: Contributor[];
}
