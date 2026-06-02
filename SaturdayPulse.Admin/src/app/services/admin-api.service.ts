import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  // Toggle between local and Azure by changing this one line
  private base = 'https://localhost:7010/api';

  constructor(private http: HttpClient) {}

  // ── Diagnostics ────────────────────────────────────────────────
  getDiagnostic(): Observable<any> {
    return this.http.get(`${this.base}/productiongamedata/diagnostic`);
  }

  // ── Weekly Ops ─────────────────────────────────────────────────
  weeklyRefresh(year: number, week: number): Observable<any> {
    return this.http.post(`${this.base}/developer/weeklyRefresh`, null,
      { params: { year, week } });
  }

  updateTeamRecords(year?: number): Observable<any> {
    const params: any = year ? { year } : {};
    return this.http.post(`${this.base}/developer/updateTeamRecords`, null, { params });
  }

  updateWeeklyMetrics(year?: number, week?: number): Observable<any> {
    const params: any = {};
    if (year) params['year'] = year;
    if (week) params['week'] = week;
    return this.http.post(`${this.base}/developer/updateWeeklyMetrics`, null, { params });
  }

  computeWeekly(year?: number, week?: number): Observable<any> {
    const params: any = {};
    if (year) params['year'] = year;
    if (week) params['week'] = week;
    return this.http.post(`${this.base}/developer/computeweekly`, null, { params });
  }

  calculateRollingAverages(year?: number, week?: number): Observable<any> {
    const params: any = {};
    if (year) params['year'] = year;
    if (week) params['week'] = week;
    return this.http.post(`${this.base}/developer/calculateRollingAverages`, null, { params });
  }

  assignPostseasonWeeks(year: number): Observable<any> {
    return this.http.post(`${this.base}/developer/assignPostseasonWeeks`, null,
      { params: { year } });
  }

  // ── Postseason Tagging ─────────────────────────────────────────
  getPostseasonGames(year: number): Observable<any> {
    return this.http.get(`${this.base}/productiongamedata/postseason/v2`, { params: { year } });
  }

  tagAsPlayoff(gameIds: number[]): Observable<any> {
    return this.http.post(`${this.base}/developer/tagAsPlayoff`, { gameIds });
  }

  untagAsPlayoff(gameIds: number[]): Observable<any> {
    return this.http.post(`${this.base}/developer/untagAsPlayoff`, { gameIds });
  }

  // ── Season Setup ───────────────────────────────────────────────
  initializeSeason(year: number): Observable<any> {
    return this.http.post(`${this.base}/developer/initializeSeason`, null, { params: { year } });
  }

  loadConferences(): Observable<any> {
    return this.http.post(`${this.base}/developer/loadConferences`, null);
  }

  loadTeams(year?: number): Observable<any> {
    const params: any = year ? { year } : {};
    return this.http.post(`${this.base}/developer/loadTeams`, null, { params });
  }

  buildTeamsConferenceHistory(startYear: number): Observable<any> {
    return this.http.post(`${this.base}/developer/buildTeamsConferenceHistory`, null,
      { params: { startYear } });
  }

  loadPortal(season: number): Observable<any> {
    return this.http.post(`${this.base}/developer/loadPortal`, null, { params: { season } });
  }

  computePortalMetrics(season: number): Observable<any> {
    return this.http.post(`${this.base}/developer/computePortalMetrics`, null, { params: { season } });
  }

  // ── Metrics Rebuild ────────────────────────────────────────────
  backfillAllMetrics(startYear?: number): Observable<any> {
    const params: any = startYear ? { startYear } : {};
    return this.http.post(`${this.base}/developer/backfillAllMetrics`, null, { params });
  }

  backfillRollingAverages(startYear?: number): Observable<any> {
    const params: any = startYear ? { startYear } : {};
    return this.http.post(`${this.base}/developer/backfillRollingAverages`, null, { params });
  }

  backfillWeeklyRankings(startYear?: number): Observable<any> {
    const params: any = startYear ? { startYear } : {};
    return this.http.post(`${this.base}/developer/backfillWeeklyRankings`, null, { params });
  }

  backfillProjections(startYear?: number): Observable<any> {
    const params: any = startYear ? { startYear } : {};
    return this.http.post(`${this.base}/developer/backfillProjections`, null, { params });
  }

  buildAvgScoreDifferentials(startYear?: number): Observable<any> {
    const params: any = startYear ? { startYear } : {};
    return this.http.post(`${this.base}/developer/buildAvgScoreDifferentials`, null, { params });
  }

  recalculateScoreDeltas(): Observable<any> {
    return this.http.post(`${this.base}/developer/recalculateScoreDeltas`, null);
  }

  calculateMatchupHistories(): Observable<any> {
    return this.http.post(`${this.base}/developer/calculateMatchupHistories`, null);
  }

  // ── Analytics ──────────────────────────────────────────────────
  getProjectionAccuracy(startYear?: number, endYear?: number): Observable<any> {
    const params: any = {};
    if (startYear) params['startYear'] = startYear;
    if (endYear) params['endYear'] = endYear;
    return this.http.get(`${this.base}/developer/projectionAccuracy`, { params });
  }

  getAnalytics(startYear?: number, endYear?: number): Observable<any> {
    const params: any = {};
    if (startYear) params['startYear'] = startYear;
    if (endYear) params['endYear'] = endYear;
    return this.http.get(`${this.base}/developer/analytics`, { params });
  }
}
