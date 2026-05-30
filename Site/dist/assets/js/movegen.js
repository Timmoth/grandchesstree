/*
 * Vanilla-JS chess move generator.
 *
 * Follows the design in /articles/move-generation/ — bitboards (LERF, a1=0),
 * 12 piece bitboards + 64-square mailbox, 16-bit packed move (from‖to‖flag),
 * classical ray-scan sliders, copy-make, legal moves via
 * checkers / pinned / king-danger trinity.
 *
 * Exposes window.GCT.MoveGen. See API summary at the bottom of the file.
 *
 * Correctness target: perft matches CPW reference numbers for the six
 * standard positions. See WebApp/perft-test.html.
 */

(function () {
  "use strict";

  // ---------------- Bitboard primitives ----------------

  const MASK64 = (1n << 64n) - 1n;
  const ONE = 1n;
  const ZERO = 0n;

  const bit = (sq) => ONE << BigInt(sq);

  function popcount(bb) {
    let n = 0;
    let v = bb;
    while (v !== ZERO) { v &= v - ONE; n++; }
    return n;
  }

  // Bitscan-forward (LSB). Caller must ensure bb !== 0n.
  function lsbIndex(bb) {
    let v = bb, idx = 0;
    if ((v & 0xFFFFFFFFn) === ZERO) { v >>= 32n; idx += 32; }
    if ((v & 0xFFFFn)     === ZERO) { v >>= 16n; idx += 16; }
    if ((v & 0xFFn)       === ZERO) { v >>=  8n; idx +=  8; }
    if ((v & 0xFn)        === ZERO) { v >>=  4n; idx +=  4; }
    if ((v & 0x3n)        === ZERO) { v >>=  2n; idx +=  2; }
    if ((v & 0x1n)        === ZERO) {            idx +=  1; }
    return idx;
  }

  function msbIndex(bb) {
    // Caller must ensure bb !== 0n.
    let n = bb;
    let idx = 0;
    if (n > 0xFFFFFFFFn) { n >>= 32n; idx += 32; }
    if (n > 0xFFFFn)     { n >>= 16n; idx += 16; }
    if (n > 0xFFn)       { n >>= 8n;  idx += 8;  }
    if (n > 0xFn)        { n >>= 4n;  idx += 4;  }
    if (n > 0x3n)        { n >>= 2n;  idx += 2;  }
    if (n > 0x1n)        { n >>= 1n;  idx += 1;  }
    return idx;
  }

  // ---------------- Constants ----------------

  const STARTPOS_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

  // Piece indices.
  const WP=0, WN=1, WB=2, WR=3, WQ=4, WK=5,
        BP=6, BN=7, BB_=8, BR=9, BQ=10, BK=11;
  const PIECE = { WP, WN, WB, WR, WQ, WK, BP, BN, BB: BB_, BR, BQ, BK };
  const NONE = -1;

  const PIECE_CHARS = ["P","N","B","R","Q","K","p","n","b","r","q","k"];
  const PIECE_FROM_CHAR = {
    P: WP, N: WN, B: WB, R: WR, Q: WQ, K: WK,
    p: BP, n: BN, b: BB_, r: BR, q: BQ, k: BK,
  };

  // Castling rights bits.
  const CR_WK = 1, CR_WQ = 2, CR_BK = 4, CR_BQ = 8;

  // Move flags (must match Move.cs).
  const F = {
    Quiet: 0, DoublePawnPush: 1, KingsideCastle: 2, QueensideCastle: 3,
    Capture: 4, EnPassant: 5,
    PromoKnight: 8,  PromoBishop: 9,  PromoRook: 10, PromoQueen: 11,
    PromoCapKnight: 12, PromoCapBishop: 13, PromoCapRook: 14, PromoCapQueen: 15,
  };

  // File / rank masks.
  const FILE_A = 0x0101010101010101n;
  const FILE_H = 0x8080808080808080n;
  const NOT_A = MASK64 ^ FILE_A;
  const NOT_H = MASK64 ^ FILE_H;
  const RANK_1 = 0x00000000000000ffn;
  const RANK_2 = 0x000000000000ff00n;
  const RANK_3 = 0x0000000000ff0000n;
  const RANK_4 = 0x00000000ff000000n;
  const RANK_5 = 0x000000ff00000000n;
  const RANK_6 = 0x0000ff0000000000n;
  const RANK_7 = 0x00ff000000000000n;
  const RANK_8 = 0xff00000000000000n;

  // Directional shifts (LERF).
  const shN  = (b) => (b << 8n) & MASK64;
  const shS  = (b) => b >> 8n;
  const shE  = (b) => (b << 1n) & NOT_A & MASK64;
  const shW  = (b) => (b >> 1n) & NOT_H;
  const shNE = (b) => (b << 9n) & NOT_A & MASK64;
  const shNW = (b) => (b << 7n) & NOT_H & MASK64;
  const shSE = (b) => (b >> 7n) & NOT_A;
  const shSW = (b) => (b >> 9n) & NOT_H;

  // Direction indices for the ray table.
  const D_N = 0, D_NE = 1, D_E = 2, D_SE = 3, D_S = 4, D_SW = 5, D_W = 6, D_NW = 7;
  const POSITIVE_DIRS = [D_N, D_NE, D_E, D_NW]; // direction-increasing → first blocker is LSB
  const NEGATIVE_DIRS = [D_S, D_SE, D_W, D_SW]; // direction-decreasing → first blocker is MSB

  // Square helpers.
  const fileOf = (sq) => sq & 7;
  const rankOf = (sq) => sq >> 3;
  const squareOf = (file, rank) => rank * 8 + file;
  function squareName(sq) {
    return String.fromCharCode(97 + (sq & 7)) + String.fromCharCode(49 + (sq >> 3));
  }
  function squareFromName(s) {
    if (typeof s !== "string" || s.length !== 2) return -1;
    const f = s.charCodeAt(0) - 97;
    const r = s.charCodeAt(1) - 49;
    if (f < 0 || f > 7 || r < 0 || r > 7) return -1;
    return r * 8 + f;
  }

  // ---------------- Pre-computed tables ----------------

  const KNIGHT_ATTACKS = new Array(64);
  const KING_ATTACKS = new Array(64);
  const PAWN_ATTACKS = [new Array(64), new Array(64)]; // [color][sq]
  const RAY = [new Array(64), new Array(64), new Array(64), new Array(64),
               new Array(64), new Array(64), new Array(64), new Array(64)];
  const BETWEEN = []; // BETWEEN[a*64+b]
  const LINE = [];    // LINE[a*64+b] = squares of the line a-b extended to board edges (incl a,b)
  const CASTLE_MASK_SQ = new Int8Array(64);

  (function initTables() {
    // Knight
    for (let sq = 0; sq < 64; sq++) {
      const b = bit(sq);
      let a = ZERO;
      a |= ((b << 17n) & MASK64) & NOT_A;
      a |= ((b << 15n) & MASK64) & NOT_H;
      a |= ((b << 10n) & MASK64) & (NOT_A & ((MASK64 ^ FILE_A) >> 0n)) & ~FILE_A & ((MASK64 ^ FILE_A << 1n)); // adjusted below
      // Simpler: use the parallel-prefix trick from the article.
      const k = b;
      const l1 = (k >> 1n) & 0x7f7f7f7f7f7f7f7fn;
      const l2 = (k >> 2n) & 0x3f3f3f3f3f3f3f3fn;
      const r1 = ((k << 1n) & MASK64) & 0xfefefefefefefefen;
      const r2 = ((k << 2n) & MASK64) & 0xfcfcfcfcfcfcfcfcn;
      const h1 = l1 | r1;
      const h2 = l2 | r2;
      KNIGHT_ATTACKS[sq] = ((h1 << 16n) & MASK64) | (h1 >> 16n) | ((h2 << 8n) & MASK64) | (h2 >> 8n);
    }

    // King
    for (let sq = 0; sq < 64; sq++) {
      const b = bit(sq);
      const horiz = shE(b) | shW(b);
      const withHoriz = b | horiz;
      KING_ATTACKS[sq] = horiz | shN(withHoriz) | shS(withHoriz);
    }

    // Pawn attacks
    for (let sq = 0; sq < 64; sq++) {
      const b = bit(sq);
      PAWN_ATTACKS[0][sq] = shNE(b) | shNW(b); // white pawn at sq attacks
      PAWN_ATTACKS[1][sq] = shSE(b) | shSW(b); // black pawn at sq attacks
    }

    // Rays — direction order [N, NE, E, SE, S, SW, W, NW].
    const DR = [ 1,  1,  0, -1, -1, -1,  0,  1];
    const DF = [ 0,  1,  1,  1,  0, -1, -1, -1];
    for (let dir = 0; dir < 8; dir++) {
      for (let sq = 0; sq < 64; sq++) {
        let r = sq >> 3, f = sq & 7;
        let ray = ZERO;
        for (;;) {
          r += DR[dir]; f += DF[dir];
          if (r < 0 || r > 7 || f < 0 || f > 7) break;
          ray |= bit(r * 8 + f);
        }
        RAY[dir][sq] = ray;
      }
    }

    // Between + line tables.
    for (let a = 0; a < 64; a++) {
      for (let b = 0; b < 64; b++) {
        let bw = ZERO, ln = ZERO;
        if (a !== b) {
          const ra = a >> 3, fa = a & 7, rb = b >> 3, fb = b & 7;
          const dr = Math.sign(rb - ra), df = Math.sign(fb - fa);
          const aligned = ra === rb || fa === fb || Math.abs(ra - rb) === Math.abs(fa - fb);
          if (aligned) {
            // Walk from a to b (exclusive) for BETWEEN.
            let r = ra + dr, f = fa + df;
            while (r !== rb || f !== fb) {
              bw |= bit(r * 8 + f);
              r += dr; f += df;
            }
            // Extend the line in both directions to the board edges (inclusive of a and b).
            ln = bit(a) | bit(b) | bw;
            let r2 = ra - dr, f2 = fa - df;
            while (r2 >= 0 && r2 < 8 && f2 >= 0 && f2 < 8) {
              ln |= bit(r2 * 8 + f2); r2 -= dr; f2 -= df;
            }
            let r3 = rb + dr, f3 = fb + df;
            while (r3 >= 0 && r3 < 8 && f3 >= 0 && f3 < 8) {
              ln |= bit(r3 * 8 + f3); r3 += dr; f3 += df;
            }
          }
        }
        BETWEEN[a * 64 + b] = bw;
        LINE[a * 64 + b] = ln;
      }
    }

    // Castling-rights-by-square mask (clear specific rights when a piece moves to or from these squares).
    for (let i = 0; i < 64; i++) CASTLE_MASK_SQ[i] = 0xF;
    CASTLE_MASK_SQ[0]  &= ~CR_WQ;             // a1
    CASTLE_MASK_SQ[7]  &= ~CR_WK;             // h1
    CASTLE_MASK_SQ[4]  &= ~(CR_WK | CR_WQ);   // e1
    CASTLE_MASK_SQ[56] &= ~CR_BQ;             // a8
    CASTLE_MASK_SQ[63] &= ~CR_BK;             // h8
    CASTLE_MASK_SQ[60] &= ~(CR_BK | CR_BQ);   // e8
  })();

  // ---------------- Slider attacks (classical ray scan) ----------------

  function positiveRay(sq, dir, occ) {
    let a = RAY[dir][sq];
    const blockers = a & occ;
    if (blockers !== ZERO) a ^= RAY[dir][lsbIndex(blockers)];
    return a;
  }
  function negativeRay(sq, dir, occ) {
    let a = RAY[dir][sq];
    const blockers = a & occ;
    if (blockers !== ZERO) a ^= RAY[dir][msbIndex(blockers)];
    return a;
  }
  function rookAttacks(sq, occ) {
    return positiveRay(sq, D_N, occ) | negativeRay(sq, D_S, occ) |
           positiveRay(sq, D_E, occ) | negativeRay(sq, D_W, occ);
  }
  function bishopAttacks(sq, occ) {
    return positiveRay(sq, D_NE, occ) | negativeRay(sq, D_SE, occ) |
           positiveRay(sq, D_NW, occ) | negativeRay(sq, D_SW, occ);
  }
  function queenAttacks(sq, occ) {
    return rookAttacks(sq, occ) | bishopAttacks(sq, occ);
  }

  // ---------------- isAttackedBy ----------------

  function attackersTo(pos, sq, byWhite) {
    const occ = pos.allOccupied;
    const off = byWhite ? 0 : 6;
    const pawns   = pos.pieces[off + 0];
    const knights = pos.pieces[off + 1];
    const bishops = pos.pieces[off + 2];
    const rooks   = pos.pieces[off + 3];
    const queens  = pos.pieces[off + 4];
    const king    = pos.pieces[off + 5];
    let attackers = ZERO;
    // Pawn lookup uses the opposite colour's attack table (asking "who attacks sq?").
    attackers |= PAWN_ATTACKS[byWhite ? 1 : 0][sq] & pawns;
    attackers |= KNIGHT_ATTACKS[sq] & knights;
    attackers |= KING_ATTACKS[sq] & king;
    attackers |= bishopAttacks(sq, occ) & (bishops | queens);
    attackers |= rookAttacks(sq, occ)   & (rooks   | queens);
    return attackers;
  }

  function isAttacked(pos, sq, byWhite) {
    return attackersTo(pos, sq, byWhite) !== ZERO;
  }

  // ---------------- Position ----------------

  function emptyPosition() {
    return {
      pieces: [ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO,ZERO],
      squares: new Int8Array(64).fill(NONE),
      whiteOccupied: ZERO, blackOccupied: ZERO, allOccupied: ZERO,
      whiteToMove: true, castling: 0, epSquare: -1,
      halfmoveClock: 0, fullmoveNumber: 1,
    };
  }

  function clonePosition(p) {
    return {
      pieces: p.pieces.slice(),
      squares: new Int8Array(p.squares),
      whiteOccupied: p.whiteOccupied,
      blackOccupied: p.blackOccupied,
      allOccupied: p.allOccupied,
      whiteToMove: p.whiteToMove,
      castling: p.castling,
      epSquare: p.epSquare,
      halfmoveClock: p.halfmoveClock,
      fullmoveNumber: p.fullmoveNumber,
    };
  }

  function rebuildOccupancy(p) {
    let w = ZERO, b = ZERO;
    for (let i = 0; i < 6;  i++) w |= p.pieces[i];
    for (let i = 6; i < 12; i++) b |= p.pieces[i];
    p.whiteOccupied = w; p.blackOccupied = b; p.allOccupied = w | b;
  }

  // ---------------- FEN ----------------

  function parseFen(fen) {
    try { return parseFenStrict(fen); } catch (e) { return null; }
  }

  function parseFenStrict(fen) {
    if (typeof fen !== "string") throw new Error("FEN must be a string");
    const parts = fen.trim().split(/\s+/);
    if (parts.length < 4) throw new Error("FEN needs at least 4 fields");
    const pos = emptyPosition();
    const ranks = parts[0].split("/");
    if (ranks.length !== 8) throw new Error(`expected 8 ranks, got ${ranks.length}`);
    for (let i = 0; i < 8; i++) {
      const realRank = 7 - i;
      let file = 0;
      for (const ch of ranks[i]) {
        if (ch >= "1" && ch <= "8") {
          file += ch.charCodeAt(0) - 48;
        } else if (ch in PIECE_FROM_CHAR) {
          if (file > 7) throw new Error(`rank ${realRank + 1}: too many squares`);
          const sq = realRank * 8 + file;
          const piece = PIECE_FROM_CHAR[ch];
          pos.squares[sq] = piece;
          pos.pieces[piece] |= bit(sq);
          file++;
        } else {
          throw new Error(`rank ${realRank + 1}: invalid char '${ch}'`);
        }
      }
      if (file !== 8) throw new Error(`rank ${realRank + 1}: ${file} squares, expected 8`);
    }
    if (parts[1] !== "w" && parts[1] !== "b") throw new Error("side must be 'w' or 'b'");
    pos.whiteToMove = parts[1] === "w";
    pos.castling = 0;
    if (parts[2] !== "-") {
      for (const c of parts[2]) {
        if (c === "K") pos.castling |= CR_WK;
        else if (c === "Q") pos.castling |= CR_WQ;
        else if (c === "k") pos.castling |= CR_BK;
        else if (c === "q") pos.castling |= CR_BQ;
        else throw new Error(`invalid castling char '${c}'`);
      }
    }
    pos.epSquare = parts[3] === "-" ? -1 : squareFromName(parts[3]);
    if (pos.epSquare === -1 && parts[3] !== "-") throw new Error(`bad ep square '${parts[3]}'`);
    pos.halfmoveClock  = parts.length > 4 ? parseInt(parts[4], 10) : 0;
    pos.fullmoveNumber = parts.length > 5 ? parseInt(parts[5], 10) : 1;
    if (!Number.isFinite(pos.halfmoveClock)  || pos.halfmoveClock < 0)
      throw new Error("bad halfmove clock");
    if (!Number.isFinite(pos.fullmoveNumber) || pos.fullmoveNumber < 0)
      throw new Error("bad fullmove number");
    rebuildOccupancy(pos);
    return pos;
  }

  function writeFen(p) {
    let out = "";
    for (let r = 7; r >= 0; r--) {
      let empties = 0;
      for (let f = 0; f < 8; f++) {
        const pc = p.squares[r * 8 + f];
        if (pc === NONE) { empties++; continue; }
        if (empties > 0) { out += empties; empties = 0; }
        out += PIECE_CHARS[pc];
      }
      if (empties > 0) out += empties;
      if (r > 0) out += "/";
    }
    out += " " + (p.whiteToMove ? "w" : "b");
    let cs = "";
    if (p.castling & CR_WK) cs += "K";
    if (p.castling & CR_WQ) cs += "Q";
    if (p.castling & CR_BK) cs += "k";
    if (p.castling & CR_BQ) cs += "q";
    out += " " + (cs || "-");
    out += " " + (p.epSquare < 0 ? "-" : squareName(p.epSquare));
    out += " " + p.halfmoveClock + " " + p.fullmoveNumber;
    return out;
  }

  // ---------------- Move encoding ----------------

  const makeMoveValue = (from, to, flag) =>
    ((flag & 0xF) << 12) | ((from & 0x3F) << 6) | (to & 0x3F);
  const moveFrom = (m) => (m >> 6) & 0x3F;
  const moveTo   = (m) => m & 0x3F;
  const moveFlag = (m) => (m >> 12) & 0xF;
  const isCapture   = (m) => (moveFlag(m) & 4) !== 0;
  const isPromotion = (m) => (moveFlag(m) & 8) !== 0;
  const isEnPassant = (m) => moveFlag(m) === F.EnPassant;
  const isCastle    = (m) => moveFlag(m) === F.KingsideCastle || moveFlag(m) === F.QueensideCastle;
  const promotionPieceIndex = (m) => moveFlag(m) & 3; // 0=N, 1=B, 2=R, 3=Q

  function uciOf(m) {
    let s = squareName(moveFrom(m)) + squareName(moveTo(m));
    if (isPromotion(m)) s += "nbrq"[promotionPieceIndex(m)];
    return s;
  }

  // ---------------- Legal move generation ----------------
  // Implements Part 7 (checkers, pinned, king-danger) on top of classical sliders.

  function computeKingDanger(pos, kingSq, enemyWhite) {
    const occWithoutKing = pos.allOccupied ^ bit(kingSq);
    let danger = ZERO;
    const off = enemyWhite ? 0 : 6;

    let pawns = pos.pieces[off + 0];
    while (pawns !== ZERO) {
      const sq = lsbIndex(pawns);
      danger |= PAWN_ATTACKS[enemyWhite ? 0 : 1][sq];
      pawns &= pawns - ONE;
    }
    let knights = pos.pieces[off + 1];
    while (knights !== ZERO) {
      const sq = lsbIndex(knights);
      danger |= KNIGHT_ATTACKS[sq];
      knights &= knights - ONE;
    }
    let bq = pos.pieces[off + 2] | pos.pieces[off + 4];
    while (bq !== ZERO) {
      const sq = lsbIndex(bq);
      danger |= bishopAttacks(sq, occWithoutKing);
      bq &= bq - ONE;
    }
    let rq = pos.pieces[off + 3] | pos.pieces[off + 4];
    while (rq !== ZERO) {
      const sq = lsbIndex(rq);
      danger |= rookAttacks(sq, occWithoutKing);
      rq &= rq - ONE;
    }
    const eksq = lsbIndex(pos.pieces[off + 5]);
    danger |= KING_ATTACKS[eksq];
    return danger;
  }

  function computeCheckers(pos, kingSq, enemyWhite) {
    // Use attackersTo with the enemy as the attacker side.
    return attackersTo(pos, kingSq, enemyWhite);
  }

  // Compute (pinned bitboard, pinLines[64]).
  function computePins(pos, kingSq, enemyWhite) {
    const pinLines = new Array(64);
    for (let i = 0; i < 64; i++) pinLines[i] = MASK64;
    let pinned = ZERO;
    const us = pos.whiteToMove ? pos.whiteOccupied : pos.blackOccupied;
    const occ = pos.allOccupied;
    const off = enemyWhite ? 0 : 6;
    const enemyRQ = pos.pieces[off + 3] | pos.pieces[off + 4];
    const enemyBQ = pos.pieces[off + 2] | pos.pieces[off + 4];

    pinned |= scanPinners(kingSq, us, occ, enemyRQ, false, pinLines);
    pinned |= scanPinners(kingSq, us, occ, enemyBQ, true,  pinLines);
    return { pinned, pinLines };
  }

  function scanPinners(kingSq, us, occ, enemySliders, isBishop, pinLines) {
    const directAttacks = isBishop ? bishopAttacks(kingSq, occ) : rookAttacks(kingSq, occ);
    const removed = directAttacks & us;
    const throughOccupancy = occ ^ removed;
    const xrayAttacks = isBishop
      ? bishopAttacks(kingSq, throughOccupancy)
      : rookAttacks  (kingSq, throughOccupancy);
    let xrayPinners = xrayAttacks & enemySliders & ~directAttacks;

    let pinned = ZERO;
    while (xrayPinners !== ZERO) {
      const pinnerSq = lsbIndex(xrayPinners);
      const between = BETWEEN[kingSq * 64 + pinnerSq];
      const ourBlocker = between & us;
      if (ourBlocker !== ZERO) {
        const pinnedSq = lsbIndex(ourBlocker);
        pinned |= ourBlocker;
        pinLines[pinnedSq] = between | bit(pinnerSq);
      }
      xrayPinners &= xrayPinners - ONE;
    }
    return pinned;
  }

  function generateLegalMoves(pos) {
    const out = [];
    const white = pos.whiteToMove;
    const us = white ? 0 : 1;
    const ourOff = white ? 0 : 6;
    const ourPieces  = white ? pos.whiteOccupied : pos.blackOccupied;
    const enemyPieces = white ? pos.blackOccupied : pos.whiteOccupied;
    const occ = pos.allOccupied;

    const kingSq = lsbIndex(pos.pieces[ourOff + 5]);
    const enemyWhite = !white;

    const kingDanger = computeKingDanger(pos, kingSq, enemyWhite);
    const checkers = computeCheckers(pos, kingSq, enemyWhite);
    const { pinned, pinLines } = computePins(pos, kingSq, enemyWhite);
    const numCheckers = popcount(checkers);

    // King moves first.
    {
      const targets = KING_ATTACKS[kingSq] & ~ourPieces & ~kingDanger;
      emitTargets(out, kingSq, targets, enemyPieces);
    }

    if (numCheckers >= 2) return out; // double check → king only

    let checkMask = MASK64;
    let checkerSq = -1;
    if (numCheckers === 1) {
      checkerSq = lsbIndex(checkers);
      checkMask = checkers | BETWEEN[kingSq * 64 + checkerSq];
    }

    // Knights — pinned knights cannot move legally.
    {
      let knights = pos.pieces[ourOff + 1] & ~pinned;
      while (knights !== ZERO) {
        const from = lsbIndex(knights);
        const targets = KNIGHT_ATTACKS[from] & ~ourPieces & checkMask;
        emitTargets(out, from, targets, enemyPieces);
        knights &= knights - ONE;
      }
    }

    // Sliders.
    emitSlider(out, pos.pieces[ourOff + 2], false, ourPieces, enemyPieces, occ, checkMask, pinned, pinLines);
    emitSlider(out, pos.pieces[ourOff + 3], true,  ourPieces, enemyPieces, occ, checkMask, pinned, pinLines);
    emitSliderQueen(out, pos.pieces[ourOff + 4], ourPieces, enemyPieces, occ, checkMask, pinned, pinLines);

    // Pawns.
    emitPawns(pos, out, white, checkMask, pinned, pinLines, kingSq, enemyWhite, checkers, checkerSq);

    // Castling — only when not in check.
    if (numCheckers === 0) emitCastling(pos, out, white, kingDanger);

    return out;
  }

  function emitTargets(out, from, targets, enemy) {
    let t = targets;
    while (t !== ZERO) {
      const to = lsbIndex(t);
      const cap = (bit(to) & enemy) !== ZERO;
      out.push(makeMoveValue(from, to, cap ? F.Capture : F.Quiet));
      t &= t - ONE;
    }
  }

  function emitSlider(out, pieces, isRook, ourPieces, enemy, occ, checkMask, pinned, pinLines) {
    let p = pieces;
    while (p !== ZERO) {
      const from = lsbIndex(p);
      let attacks = (isRook ? rookAttacks(from, occ) : bishopAttacks(from, occ)) & ~ourPieces & checkMask;
      if ((bit(from) & pinned) !== ZERO) attacks &= pinLines[from];
      emitTargets(out, from, attacks, enemy);
      p &= p - ONE;
    }
  }

  function emitSliderQueen(out, pieces, ourPieces, enemy, occ, checkMask, pinned, pinLines) {
    let p = pieces;
    while (p !== ZERO) {
      const from = lsbIndex(p);
      let attacks = queenAttacks(from, occ) & ~ourPieces & checkMask;
      if ((bit(from) & pinned) !== ZERO) attacks &= pinLines[from];
      emitTargets(out, from, attacks, enemy);
      p &= p - ONE;
    }
  }

  function emitPawns(pos, out, white, checkMask, pinned, pinLines, kingSq, enemyWhite, checkers, checkerSq) {
    const off = white ? 0 : 6;
    const pawns = pos.pieces[off + 0];
    const empty = (~pos.allOccupied) & MASK64;
    const enemy = white ? pos.blackOccupied : pos.whiteOccupied;
    const promoRank = white ? RANK_8 : RANK_1;
    const startMid  = white ? RANK_3 : RANK_6;
    const dPush = white ? -8 : 8;
    const dCapE = white ? -9 : 7;
    const dCapW = white ? -7 : 9;

    let singles = (white ? shN(pawns) : shS(pawns)) & empty;
    let doubles = (white ? shN(singles & startMid) : shS(singles & startMid)) & empty;
    let capE = (white ? shNE(pawns) : shSE(pawns)) & enemy;
    let capW = (white ? shNW(pawns) : shSW(pawns)) & enemy;

    singles &= checkMask;
    doubles &= checkMask;
    capE &= checkMask;
    capW &= checkMask;

    emitPawnList(out, singles & ~promoRank, dPush, F.Quiet, pinned, pinLines);
    emitPromoList(out, singles & promoRank, dPush, false, pinned, pinLines);
    emitPawnList(out, doubles, 2 * dPush, F.DoublePawnPush, pinned, pinLines);
    emitPawnList(out, capE & ~promoRank, dCapE, F.Capture, pinned, pinLines);
    emitPawnList(out, capW & ~promoRank, dCapW, F.Capture, pinned, pinLines);
    emitPromoList(out, capE & promoRank, dCapE, true, pinned, pinLines);
    emitPromoList(out, capW & promoRank, dCapW, true, pinned, pinLines);

    if (pos.epSquare >= 0) {
      emitEnPassant(pos, out, white, kingSq, enemyWhite, checkers, checkerSq);
    }
  }

  function emitPawnList(out, targets, deltaFrom, flag, pinned, pinLines) {
    let t = targets;
    while (t !== ZERO) {
      const to = lsbIndex(t);
      const from = to + deltaFrom;
      if ((bit(from) & pinned) === ZERO || (bit(to) & pinLines[from]) !== ZERO) {
        out.push(makeMoveValue(from, to, flag));
      }
      t &= t - ONE;
    }
  }

  function emitPromoList(out, targets, deltaFrom, capture, pinned, pinLines) {
    const baseFlag = capture ? F.PromoCapKnight : F.PromoKnight;
    let t = targets;
    while (t !== ZERO) {
      const to = lsbIndex(t);
      const from = to + deltaFrom;
      if ((bit(from) & pinned) === ZERO || (bit(to) & pinLines[from]) !== ZERO) {
        for (let i = 0; i < 4; i++) out.push(makeMoveValue(from, to, baseFlag + i));
      }
      t &= t - ONE;
    }
  }

  function emitEnPassant(pos, out, white, kingSq, enemyWhite, checkers, checkerSq) {
    const ep = pos.epSquare;
    const epBit = bit(ep);
    const off = white ? 0 : 6;
    const pawns = pos.pieces[off + 0];

    const capSq = white ? ep - 8 : ep + 8;
    // If in single check, EP is legal only if it captures the checker.
    // (See feedback note B1 — the article omits this guard.)
    if (checkerSq !== -1 && capSq !== checkerSq) return;

    let froms = ZERO;
    if ((white ? shNE(pawns) : shSE(pawns)) & epBit) froms |= bit(white ? ep - 9 : ep + 7);
    if ((white ? shNW(pawns) : shSW(pawns)) & epBit) froms |= bit(white ? ep - 7 : ep + 9);

    while (froms !== ZERO) {
      const from = lsbIndex(froms);
      // Horizontal-pin check: simulate the capture and ensure the king isn't suddenly attacked by a slider.
      const occSim = (pos.allOccupied ^ bit(from) ^ bit(ep) ^ bit(capSq)) & MASK64;
      const enemyOff = enemyWhite ? 0 : 6;
      const enemyRQ = pos.pieces[enemyOff + 3] | pos.pieces[enemyOff + 4];
      const enemyBQ = pos.pieces[enemyOff + 2] | pos.pieces[enemyOff + 4];
      const kingAttacked =
        (rookAttacks(kingSq, occSim) & enemyRQ) !== ZERO ||
        (bishopAttacks(kingSq, occSim) & enemyBQ) !== ZERO;
      if (!kingAttacked) out.push(makeMoveValue(from, ep, F.EnPassant));
      froms &= froms - ONE;
    }
  }

  function emitCastling(pos, out, white, kingDanger) {
    const occ = pos.allOccupied;
    if (white) {
      if ((pos.castling & CR_WK) &&
          (occ & 0x60n) === ZERO &&
          (kingDanger & 0x70n) === ZERO) {
        out.push(makeMoveValue(4, 6, F.KingsideCastle));
      }
      if ((pos.castling & CR_WQ) &&
          (occ & 0x0En) === ZERO &&
          (kingDanger & 0x1Cn) === ZERO) {
        out.push(makeMoveValue(4, 2, F.QueensideCastle));
      }
    } else {
      if ((pos.castling & CR_BK) &&
          (occ & 0x6000000000000000n) === ZERO &&
          (kingDanger & 0x7000000000000000n) === ZERO) {
        out.push(makeMoveValue(60, 62, F.KingsideCastle));
      }
      if ((pos.castling & CR_BQ) &&
          (occ & 0x0E00000000000000n) === ZERO &&
          (kingDanger & 0x1C00000000000000n) === ZERO) {
        out.push(makeMoveValue(60, 58, F.QueensideCastle));
      }
    }
  }

  // ---------------- applyMove (copy-make) ----------------

  function applyMove(pos, m) {
    const next = clonePosition(pos);
    const from = moveFrom(m), to = moveTo(m), flag = moveFlag(m);
    const moving = next.squares[from];
    if (moving === NONE) return null; // safety
    const white = next.whiteToMove;

    // Capture target (default to dest; EP target is one rank back).
    let capturedSq = (flag === F.EnPassant) ? (white ? to - 8 : to + 8) : to;
    let captured = (flag === F.EnPassant)
      ? (white ? BP : WP)
      : (((flag & 4) !== 0) ? next.squares[to] : NONE);

    // Castling rights update.
    next.castling &= CASTLE_MASK_SQ[from] & CASTLE_MASK_SQ[to];

    // Halfmove clock.
    const isPawn = moving === WP || moving === BP;
    next.halfmoveClock = (isPawn || (flag & 4) !== 0) ? 0 : next.halfmoveClock + 1;

    // Move the moving piece.
    const fromToMask = bit(from) | bit(to);
    next.pieces[moving] ^= fromToMask;
    next.squares[from] = NONE;
    next.squares[to] = moving;

    // Apply capture.
    if (captured !== NONE) {
      next.pieces[captured] ^= bit(capturedSq);
      if (flag === F.EnPassant) next.squares[capturedSq] = NONE;
      // For non-EP captures, squares[to] is already overwritten above with the mover.
    }

    // Special: castling — move the rook too.
    if (flag === F.KingsideCastle) {
      if (to === 6) moveRook(next, 7, 5);
      else          moveRook(next, 63, 61);
    } else if (flag === F.QueensideCastle) {
      if (to === 2) moveRook(next, 0, 3);
      else          moveRook(next, 56, 59);
    }

    // Special: promotion — replace the pawn that landed on `to` with the promo piece.
    if ((flag & 8) !== 0) {
      const pawnPiece = white ? WP : BP;
      const promoIdx = flag & 3; // 0=N, 1=B, 2=R, 3=Q
      const promoPiece = (white ? WN : BN) + promoIdx;
      next.pieces[pawnPiece]  ^= bit(to);
      next.pieces[promoPiece] ^= bit(to);
      next.squares[to] = promoPiece;
    }

    // EP square update.
    next.epSquare = (flag === F.DoublePawnPush)
      ? (white ? to - 8 : to + 8)
      : -1;

    // Side / fullmove.
    if (!white) next.fullmoveNumber += 1;
    next.whiteToMove = !white;

    rebuildOccupancy(next);
    return next;
  }

  function moveRook(p, from, to) {
    const piece = p.squares[from];
    const mask = bit(from) | bit(to);
    p.pieces[piece] ^= mask;
    p.squares[from] = NONE;
    p.squares[to] = piece;
  }

  // ---------------- Queries ----------------

  function isCheck(pos) {
    const off = pos.whiteToMove ? 0 : 6;
    const ksq = lsbIndex(pos.pieces[off + 5]);
    return isAttacked(pos, ksq, !pos.whiteToMove);
  }

  function isCheckmate(pos) { return isCheck(pos) && generateLegalMoves(pos).length === 0; }
  function isStalemate(pos) { return !isCheck(pos) && generateLegalMoves(pos).length === 0; }

  // ---------------- UCI / SAN ----------------

  function moveFromUci(pos, uci) {
    if (typeof uci !== "string" || (uci.length !== 4 && uci.length !== 5)) return null;
    const target = uci.toLowerCase();
    for (const m of generateLegalMoves(pos)) if (uciOf(m) === target) return m;
    return null;
  }

  // SAN printing: minimal disambiguation following the standard rules.
  function sanOf(pos, m) {
    const flag = moveFlag(m);
    if (flag === F.KingsideCastle)  return suffixCheck(pos, m, "O-O");
    if (flag === F.QueensideCastle) return suffixCheck(pos, m, "O-O-O");

    const from = moveFrom(m), to = moveTo(m);
    const moving = pos.squares[from];
    const isPawnMv = moving === WP || moving === BP;
    const capture = isCapture(m);
    let s = "";

    if (isPawnMv) {
      if (capture) s += "abcdefgh"[fileOf(from)] + "x";
      s += squareName(to);
      if (isPromotion(m)) s += "=" + "NBRQ"[promotionPieceIndex(m)];
    } else {
      const pieceChar = "PNBRQK"[moving % 6]; // works for both colours
      s += pieceChar;
      // Disambiguation: other same-type pieces that can also legally move to `to`.
      const sameType = generateLegalMoves(pos).filter((other) => {
        if (other === m) return false;
        if (moveTo(other) !== to) return false;
        const op = pos.squares[moveFrom(other)];
        return op === moving;
      });
      if (sameType.length > 0) {
        const fromFile = "abcdefgh"[fileOf(from)];
        const fromRank = "12345678"[rankOf(from)];
        const sameFile = sameType.some(o => fileOf(moveFrom(o)) === fileOf(from));
        const sameRank = sameType.some(o => rankOf(moveFrom(o)) === rankOf(from));
        if (!sameFile)       s += fromFile;
        else if (!sameRank)  s += fromRank;
        else                 s += fromFile + fromRank;
      }
      if (capture) s += "x";
      s += squareName(to);
    }
    return suffixCheck(pos, m, s);
  }

  function suffixCheck(pos, m, s) {
    // Apply the move, see if the new side is in check / mate.
    const next = applyMove(pos, m);
    if (!next) return s;
    if (isCheck(next)) {
      const mateNow = generateLegalMoves(next).length === 0;
      return s + (mateNow ? "#" : "+");
    }
    return s;
  }

  function moveFromSan(pos, san) {
    if (typeof san !== "string") return null;
    // Strip annotations (+, #, !, ?).
    const clean = san.replace(/[+#!?]+$/, "").trim();
    if (clean === "O-O" || clean === "0-0") {
      for (const m of generateLegalMoves(pos)) if (moveFlag(m) === F.KingsideCastle) return m;
      return null;
    }
    if (clean === "O-O-O" || clean === "0-0-0") {
      for (const m of generateLegalMoves(pos)) if (moveFlag(m) === F.QueensideCastle) return m;
      return null;
    }
    // Look for the legal move whose sanOf matches the cleaned input.
    for (const m of generateLegalMoves(pos)) {
      const printed = sanOf(pos, m).replace(/[+#]+$/, "");
      if (printed === clean) return m;
    }
    return null;
  }

  // ---------------- Perft ----------------

  function perft(pos, depth) {
    if (depth === 0) return 1n;
    const moves = generateLegalMoves(pos);
    if (depth === 1) return BigInt(moves.length);
    let nodes = 0n;
    for (const m of moves) {
      const next = applyMove(pos, m);
      nodes += perft(next, depth - 1);
    }
    return nodes;
  }

  function perftDivide(pos, depth) {
    if (depth < 1) return [];
    const moves = generateLegalMoves(pos);
    const rows = [];
    for (const m of moves) {
      const next = applyMove(pos, m);
      const sub = depth === 1 ? 1n : perft(next, depth - 1);
      rows.push({ uci: uciOf(m), nodes: sub });
    }
    rows.sort((a, b) => a.uci < b.uci ? -1 : a.uci > b.uci ? 1 : 0);
    return rows;
  }

  // ---------------- Public API ----------------

  const api = {
    STARTPOS_FEN,
    PIECE, FLAG: F,
    NONE,
    // FEN
    parseFen, parseFenStrict, writeFen,
    // Position helpers
    clonePosition,
    // Move utils
    makeMoveValue, moveFrom, moveTo, moveFlag,
    isCapture, isPromotion, isEnPassant, isCastle, promotionPieceIndex,
    uciOf, moveFromUci, sanOf, moveFromSan,
    squareName, squareFromName,
    // Queries
    isCheck, isCheckmate, isStalemate, isAttacked, attackersTo,
    // Generation + make + perft
    legalMoves: generateLegalMoves,
    applyMove,
    perft, perftDivide,
  };

  if (typeof window !== "undefined") {
    window.GCT = window.GCT || {};
    window.GCT.MoveGen = api;
  }
  if (typeof globalThis !== "undefined") {
    globalThis.GCT = globalThis.GCT || {};
    globalThis.GCT.MoveGen = api;
  }
  if (typeof module !== "undefined" && module.exports) {
    module.exports = api;
  }
})();
