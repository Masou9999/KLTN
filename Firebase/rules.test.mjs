import { initializeTestEnvironment, assertSucceeds, assertFails } from '@firebase/rules-unit-testing';
import { doc, setDoc, getDoc, writeBatch, serverTimestamp, Timestamp, collection, query, orderBy, limit, getDocs, updateDoc } from 'firebase/firestore';
import fs from 'fs';

const env = await initializeTestEnvironment({ projectId: 'demo-kltn', firestore: { rules: fs.readFileSync('firestore.rules', 'utf8'), host: '127.0.0.1', port: 8080 } });
await env.clearFirestore(); // also safe on an emulator shared with the Cloud Code tests
let pass = 0, fail = 0;
async function t(name, fn) { try { await fn(); pass++; console.log('ok  ', name); } catch (e) { fail++; console.log('FAIL', name, e.message); } }
const now = Date.now(), H = 3600e3, M = 60e3;
const db = uid => env.authenticatedContext(uid).firestore();
const zero = { coins: 0, silver: 0, schemaVersion: 1 };
const wr = (wins, matches) => matches > 0 ? Math.floor(wins * 10000 / matches) : 0;

function calendar(anchorStartMs, pending) {
  return { anchorSeasonId: 1, anchorStartMs, durationMs: 3 * H, lockMs: 30 * M,
    pendingSeasonId: pending ? pending.id : 0, pendingStartMs: pending ? pending.start : 0,
    pendingDurationMs: pending ? pending.dur : 0, pendingLockMs: pending ? pending.lock : 0 };
}
async function seed(fn) { await env.withSecurityRulesDisabled(async c => fn(c.firestore())); }
await seed(async a => {
  // 3 h seasons, 30 min lock; season 3 = [now - 1h, now + 2h), lock at now + 1.5h.
  await setDoc(doc(a, 'config/season'), calendar(now - H - 2 * 3 * H));
  await setDoc(doc(a, 'users/old'), { ...zero, createdAt: Timestamp.now(), updatedAt: Timestamp.now() });
  await setDoc(doc(a, 'users/vet'), { ...zero, username: 'user222222', createdAt: Timestamp.now(), updatedAt: Timestamp.now() });
  await setDoc(doc(a, 'usernames/user222222'), { uid: 'vet', createdAt: Timestamp.now() });
  await setDoc(doc(a, 'seasons/3/entries/vet'), { username: 'user222222', points: 10, wins: 0, losses: 1, draws: 0, matches: 1, winRate: 0, lastMatchId: 'm0', updatedAt: Timestamp.fromMillis(now - 5 * M) });
  await setDoc(doc(a, 'seasons/3/entries/vet/results/m0'), { result: 'loss', delta: -20, matchStartedAt: Timestamp.fromMillis(now - 20 * M), createdAt: Timestamp.now() });
});

function newUser(d, uid, name) {
  const b = writeBatch(d);
  b.set(doc(d, 'users/' + uid), { ...zero, username: name, createdAt: serverTimestamp(), updatedAt: serverTimestamp() });
  b.set(doc(d, 'usernames/' + name), { uid, createdAt: serverTimestamp() });
  b.set(doc(d, `users/${uid}/ownedCards/MaDa`), { source: 'starter', acquiredAt: serverTimestamp() });
  return b.commit();
}
await t('new user with username', () => assertSucceeds(newUser(db('alice'), 'alice', 'user111111')));
await t('new user without username (old client) still allowed', async () => {
  const d = db('nouser'); const b = writeBatch(d);
  b.set(doc(d, 'users/nouser'), { ...zero, createdAt: serverTimestamp(), updatedAt: serverTimestamp() });
  await assertSucceeds(b.commit());
});
await t('username taken by another uid denied', () => assertFails(newUser(db('bob'), 'bob', 'user111111')));
await t('bad username format denied', () => assertFails(newUser(db('carl'), 'carl', 'admin00001')));
await t('username doc pointing to other uid denied', async () => {
  const d = db('dave'); const b = writeBatch(d);
  b.set(doc(d, 'users/dave'), { ...zero, username: 'user333333', createdAt: serverTimestamp(), updatedAt: serverTimestamp() });
  b.set(doc(d, 'usernames/user333333'), { uid: 'alice', createdAt: serverTimestamp() });
  await assertFails(b.commit());
});
function claim(d, uid, name) {
  const b = writeBatch(d);
  b.update(doc(d, 'users/' + uid), { username: name, updatedAt: serverTimestamp() });
  b.set(doc(d, 'usernames/' + name), { uid, createdAt: serverTimestamp() });
  return b.commit();
}
await t('old user claims username', () => assertSucceeds(claim(db('old'), 'old', 'user444444')));
await t('second claim denied', () => assertFails(claim(db('old'), 'old', 'user555555')));
await t('claim username alone without user update denied', () => assertFails(setDoc(doc(db('zed'), 'usernames/user666666'), { uid: 'zed', createdAt: serverTimestamp() })));

// Server-trusted results (FR-RANK-07/11, FR-SHOP-08/09): the batch the old client wrote is now denied.
function result(d, uid, season, matchId, res, delta, entry, exists, startedMs) {
  const b = writeBatch(d);
  const e = { username: entry.username, points: entry.points, wins: entry.wins, losses: entry.losses, draws: entry.draws, matches: entry.matches, winRate: entry.winRate ?? wr(entry.wins, entry.matches), lastMatchId: matchId, updatedAt: serverTimestamp() };
  if (exists) b.update(doc(d, `seasons/${season}/entries/${uid}`), e); else b.set(doc(d, `seasons/${season}/entries/${uid}`), e);
  b.set(doc(d, `seasons/${season}/entries/${uid}/results/${matchId}`), { result: res, delta, matchStartedAt: Timestamp.fromMillis(startedMs), createdAt: serverTimestamp() });
  return b.commit();
}
const A = 'user111111';
await t('client ranked win (first row with points) denied', () => assertFails(result(db('alice'), 'alice', 3, 'm1', 'win', 30, { username: A, points: 30, wins: 1, losses: 0, draws: 0, matches: 1 }, false, now - 10 * M)));
await t('client ranked loss on existing row denied', async () => {
  await seed(a => updateDoc(doc(a, 'seasons/3/entries/vet'), { updatedAt: Timestamp.fromMillis(now - 5 * M) }));
  await assertFails(result(db('vet'), 'vet', 3, 'm3', 'loss', -20, { username: 'user222222', points: 0, wins: 0, losses: 2, draws: 0, matches: 2 }, true, now - 10 * M));
});
await t('client edits own points directly denied', () => assertFails(updateDoc(doc(db('vet'), 'seasons/3/entries/vet'), { points: 999 })));
await t('client writes a result doc alone denied', () => assertFails(setDoc(doc(db('vet'), 'seasons/3/entries/vet/results/mz'), { result: 'win', delta: 30, matchStartedAt: Timestamp.now(), createdAt: serverTimestamp() })));
await t('client deletes own season row denied', async () => {
  const { deleteDoc } = await import('firebase/firestore');
  await assertFails(deleteDoc(doc(db('vet'), 'seasons/3/entries/vet')));
});
await t('empty row for alice in running season still allowed', () => assertSucceeds(setDoc(doc(db('alice'), 'seasons/3/entries/alice'), { username: A, points: 0, wins: 0, losses: 0, draws: 0, matches: 0, winRate: 0, updatedAt: serverTimestamp() })));
await t('owner reads own results (written by the server)', () => assertSucceeds(getDoc(doc(db('vet'), 'seasons/3/entries/vet/results/m0'))));
await t('client cannot write config', () => assertFails(setDoc(doc(db('alice'), 'config/season'), { seasonId: 99 })));
await t('client cannot write season doc', () => assertFails(setDoc(doc(db('alice'), 'seasons/3'), { seasonId: 3 })));
await t('signed-in player reads leaderboard page', async () => {
  const snap = await assertSucceeds(getDocs(query(collection(db('bob'), 'seasons/3/entries'), orderBy('points', 'desc'), orderBy('winRate', 'desc'), orderBy('username'), limit(50))));
  if (snap.size !== 2) throw new Error('expected 2 rows, got ' + snap.size);
});
await t('anonymous cannot read leaderboard', () => assertFails(getDocs(collection(env.unauthenticatedContext().firestore(), 'seasons/3/entries'))));
await t('other player cannot read my results', () => assertFails(getDoc(doc(db('bob'), 'seasons/3/entries/alice/results/m1'))));
await t('reads config/season', () => assertSucceeds(getDoc(doc(db('bob'), 'config/season'))));
// Empty rows and the leaderboard order (season 3 running again).
function empty(d, uid, season, over = {}) {
  return setDoc(doc(d, `seasons/${season}/entries/${uid}`), { username: 'user333333', points: 0, wins: 0, losses: 0, draws: 0, matches: 0, winRate: 0, updatedAt: serverTimestamp(), ...over });
}
await seed(async a => {
  await setDoc(doc(a, 'config/season'), calendar(now - H - 2 * 3 * H));
  await setDoc(doc(a, 'users/cara'), { ...zero, username: 'user333333', createdAt: Timestamp.now(), updatedAt: Timestamp.now() });
  await setDoc(doc(a, 'usernames/user333333'), { uid: 'cara', createdAt: Timestamp.now() });
});
await t('empty row with points denied', () => assertFails(empty(db('cara'), 'cara', 3, { points: 10 })));
await t('empty row with win rate denied', () => assertFails(empty(db('cara'), 'cara', 3, { winRate: 5000 })));
await t('empty row with fake username denied', () => assertFails(empty(db('cara'), 'cara', 3, { username: 'user999999' })));
await t('empty row for ended season denied', () => assertFails(empty(db('cara'), 'cara', 2)));
await t('empty row for future season denied', () => assertFails(empty(db('cara'), 'cara', 4)));
await t('empty row for other player denied', () => assertFails(empty(db('bob'), 'cara', 3)));
await t('empty row in running season (0 points, 0 %)', () => assertSucceeds(empty(db('cara'), 'cara', 3)));
await t('empty row cannot be created twice', () => assertFails(empty(db('cara'), 'cara', 3)));
await t('client scores a win on its empty row denied', async () => {
  await seed(a => updateDoc(doc(a, 'seasons/3/entries/cara'), { updatedAt: Timestamp.fromMillis(now - 2 * M) }));
  await assertFails(result(db('cara'), 'cara', 3, 'c1', 'win', 30, { username: 'user333333', points: 30, wins: 1, losses: 0, draws: 0, matches: 1 }, true, now - 10 * M));
});
await t('page order: points desc, win rate desc, username asc', async () => {
  // Rows as the server writes them: cara 30 pts 1/1, alice 10 pts 1/3, vet 0 pts 0/2, dave 10 pts 1/3 (ties alice).
  const row = (username, points, wins, matches) => ({ username, points, wins, losses: matches - wins, draws: 0, matches, winRate: wr(wins, matches), updatedAt: Timestamp.now() });
  await seed(async a => {
    await setDoc(doc(a, 'seasons/3/entries/cara'), row('user333333', 30, 1, 1));
    await setDoc(doc(a, 'seasons/3/entries/alice'), row(A, 10, 1, 3));
    await setDoc(doc(a, 'seasons/3/entries/vet'), row('user222222', 0, 0, 2));
    await setDoc(doc(a, 'seasons/3/entries/dave'), row('user000010', 10, 1, 3));
  });
  const snap = await getDocs(query(collection(db('bob'), 'seasons/3/entries'), orderBy('points', 'desc'), orderBy('winRate', 'desc'), orderBy('username'), limit(50)));
  const names = snap.docs.map(x => x.data().username).join(',');
  if (names !== 'user333333,user000010,user111111,user222222') throw new Error(names);
});
await t('economy: client reward claim (old batch) denied', async () => {
  const d = db('alice'); const b = writeBatch(d);
  const { increment } = await import('firebase/firestore');
  b.update(doc(d, 'users/alice'), { lastRewardMatchId: 'r1', coins: increment(75), silver: increment(1), updatedAt: serverTimestamp() });
  b.set(doc(d, 'users/alice/matchRewards/r1'), { result: 'win', coins: 75, silver: 1, createdAt: serverTimestamp() });
  await assertFails(b.commit());
});
await t('economy: client matchRewards doc alone denied', () => assertFails(setDoc(doc(db('alice'), 'users/alice/matchRewards/r2'), { result: 'win', coins: 75, silver: 1, createdAt: serverTimestamp() })));
await t('economy: client raises own coins denied', async () => {
  const { increment } = await import('firebase/firestore');
  await assertFails(updateDoc(doc(db('alice'), 'users/alice'), { coins: increment(1000), updatedAt: serverTimestamp() }));
});
await t('economy: client raises own silver denied', () => assertFails(updateDoc(doc(db('alice'), 'users/alice'), { silver: 99, updatedAt: serverTimestamp() })));
await t('economy: owner reads own reward (written by the server)', async () => {
  await seed(a => setDoc(doc(a, 'users/alice/matchRewards/srv1'), { result: 'win', coins: 75, silver: 1, createdAt: Timestamp.now() }));
  await assertSucceeds(getDoc(doc(db('alice'), 'users/alice/matchRewards/srv1')));
});
await t('economy: other player cannot read my rewards', () => assertFails(getDoc(doc(db('bob'), 'users/alice/matchRewards/srv1'))));
await t('economy: purchase still works', async () => {
  const d = db('alice'); const b = writeBatch(d);
  await seed(a => updateDoc(doc(a, 'users/alice'), { coins: 500 }));
  const { increment } = await import('firebase/firestore');
  b.update(doc(d, 'users/alice'), { lastPurchase: 'MaMatMam', coins: increment(-150), updatedAt: serverTimestamp() });
  b.set(doc(d, 'users/alice/ownedCards/MaMatMam'), { source: 'coins', acquiredAt: serverTimestamp() });
  await assertSucceeds(b.commit());
});
await t('economy: purchase at the wrong price denied', async () => {
  const d = db('alice'); const b = writeBatch(d);
  const { increment } = await import('firebase/firestore');
  b.update(doc(d, 'users/alice'), { lastPurchase: 'MaLai', coins: increment(-1), updatedAt: serverTimestamp() });
  b.set(doc(d, 'users/alice/ownedCards/MaLai'), { source: 'coins', acquiredAt: serverTimestamp() });
  await assertFails(b.commit());
});
await t('economy: purchase that also adds silver denied', async () => {
  const d = db('alice'); const b = writeBatch(d);
  const { increment } = await import('firebase/firestore');
  b.update(doc(d, 'users/alice'), { lastPurchase: 'MaLai', coins: increment(-200), silver: increment(5), updatedAt: serverTimestamp() });
  b.set(doc(d, 'users/alice/ownedCards/MaLai'), { source: 'coins', acquiredAt: serverTimestamp() });
  await assertFails(b.commit());
});
await t('economy: silver purchase still works', async () => {
  const d = db('alice'); const b = writeBatch(d);
  await seed(a => updateDoc(doc(a, 'users/alice'), { silver: 20 }));
  const { increment } = await import('firebase/firestore');
  b.update(doc(d, 'users/alice'), { lastPurchase: 'LinhMieu', silver: increment(-10), updatedAt: serverTimestamp() });
  b.set(doc(d, 'users/alice/ownedCards/LinhMieu'), { source: 'silver', acquiredAt: serverTimestamp() });
  await assertSucceeds(b.commit());
});
// Server-only collections.
await seed(async a => {
  await setDoc(doc(a, 'matches/mm1'), { players: ['p1', 'p2'], tokenHash: 'ab', serverId: 's', createdAt: Timestamp.now() });
  await setDoc(doc(a, 'ugsPlayers/p1'), { uid: 'alice', updatedAt: Timestamp.now() });
});
await t('client cannot read matches', () => assertFails(getDoc(doc(db('alice'), 'matches/mm1'))));
await t('client cannot create matches', () => assertFails(setDoc(doc(db('alice'), 'matches/mm2'), { players: ['p1', 'p2'], tokenHash: 'x' })));
await t('client cannot settle a match', () => assertFails(updateDoc(doc(db('alice'), 'matches/mm1'), { reportedAt: serverTimestamp() })));
await t('client cannot read ugsPlayers', () => assertFails(getDoc(doc(db('alice'), 'ugsPlayers/p1'))));
await t('client cannot link a player to its uid', () => assertFails(setDoc(doc(db('bob'), 'ugsPlayers/p9'), { uid: 'bob' })));
await env.cleanup();
console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
