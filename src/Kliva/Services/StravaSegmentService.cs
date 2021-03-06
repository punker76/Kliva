﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kliva.Helpers;
using Kliva.Models;
using Kliva.Services.Interfaces;

namespace Kliva.Services
{
    public class StravaSegmentService : IStravaSegmentService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogService _logService;
        private readonly StravaWebClient _stravaWebClient;

        private readonly ConcurrentDictionary<string, Task<List<SegmentSummary>>> _cachedStarredSegmentsTasks = new ConcurrentDictionary<string, Task<List<SegmentSummary>>>();

        //TODO: Glenn - How long before we invalidate an in memory cached segment? Maybe use MemoryCache? https://msdn.microsoft.com/en-us/library/system.runtime.caching.memorycache(v=vs.110).aspx
        private readonly ConcurrentDictionary<string, Task<Segment>> _cachedSegmentTasks = new ConcurrentDictionary<string, Task<Segment>>();
        private readonly ConcurrentDictionary<string, Task<SegmentEffort>> _cachedSegmentEffortTasks = new ConcurrentDictionary<string, Task<SegmentEffort>>();
        private readonly ConcurrentDictionary<string, Task<Leaderboard>> _cachedLeaderboardTasks = new ConcurrentDictionary<string, Task<Leaderboard>>();
        private readonly ConcurrentDictionary<string, Task<Leaderboard>> _cachedLeaderboardFollowingTasks = new ConcurrentDictionary<string, Task<Leaderboard>>();

        public StravaSegmentService(ISettingsService settingsService, ILogService logService, StravaWebClient stravaWebClient)
        {
            _settingsService = settingsService;
            _logService = logService;
            _stravaWebClient = stravaWebClient;
        }

        private static void FillStatistics(SegmentEffort segmentEffort)
        {
            StatisticsGroup current = new StatisticsGroup() { Name = "this effort", Sort = 0, Type = StatisticGroupType.Current};
            StatisticsDetail movingTimeCurrent = new StatisticsDetail()
            {
                Sort = 0,
                Icon = "",
                DisplayDescription = "moving time",
                DisplayValue = $"{Helpers.Converters.SecToTimeConverter.Convert(segmentEffort.ElapsedTime, typeof(int), null, string.Empty)}",
                Group = current
            };

            StatisticsDetail averageSpeedCurrent = new UserMeasurementUnitStatisticsDetail(segmentEffort.AverageSpeedMeasurementUnit)
            {
                Sort = 1,
                Icon = "",
                DisplayDescription = "average speed",
                Group = current
            };

            StatisticsDetail averageHeartRateCurrent = new StatisticsDetail()
            {
                Sort = 2,
                Icon = "",
                DisplayDescription = "average heart rate",
                DisplayValue = $"{Math.Round(segmentEffort.AverageHeartrate)} bpm",
                Group = current
            };

            StatisticsDetail maxHeartRateCurrent = new StatisticsDetail()
            {
                Sort = 3,
                Icon = "",
                DisplayDescription = "max heart rate",
                DisplayValue = $"{segmentEffort.MaxHeartrate} bpm",
                Group = current
            };

            current.Details.Add(movingTimeCurrent);
            current.Details.Add(averageSpeedCurrent);
            current.Details.Add(averageHeartRateCurrent);
            current.Details.Add(maxHeartRateCurrent);

            segmentEffort.Statistics.Add(current);
        }

        private async Task<Segment> GetSegmentFromServiceAsync(string segmentId)
        {
            try
            {
                var accessToken = await _settingsService.GetStoredStravaAccessTokenAsync();
                var defaultDistanceUnitType = await _settingsService.GetStoredDistanceUnitTypeAsync();

                string getUrl = $"{Endpoints.Segment}/{segmentId}?access_token={accessToken}";
                string json = await _stravaWebClient.GetAsync(new Uri(getUrl));

                var segment = Unmarshaller<Segment>.Unmarshal(json);
                StravaService.SetMetricUnits(segment, defaultDistanceUnitType);

                return segment;
            }
            catch (Exception ex)
            {
                string title = $"StravaSegmentService.GetSegmentFromServiceAsync - segmentId {segmentId}";
                _logService.LogException(title, ex);
            }

            return null;
        }

        private async Task<SegmentEffort> GetSegmentEffortFromServiceAsync(string segmentEffortId)
        {
            try
            {
                var accessToken = await _settingsService.GetStoredStravaAccessTokenAsync();
                var defaultDistanceUnitType = await _settingsService.GetStoredDistanceUnitTypeAsync();

                string getUrl = $"{Endpoints.SegmentEffort}/{segmentEffortId}?access_token={accessToken}";
                string json = await _stravaWebClient.GetAsync(new Uri(getUrl));

                var segmentEffort = Unmarshaller<SegmentEffort>.Unmarshal(json);
                StravaService.SetMetricUnits(segmentEffort, defaultDistanceUnitType);

                FillStatistics(segmentEffort);

                return segmentEffort;
            }
            catch (Exception ex)
            {
                string title = $"StravaSegmentService.GetSegmentEffortFromServiceAsync - segmentEffortId {segmentEffortId}";
                _logService.LogException(title, ex);
            }

            return null;
        }

        private Task<Leaderboard> GetLeaderboardFollowingFromServiceAsync(string segmentId)
        {
            return GetLeaderboardFromServiceAsync(segmentId, true);
        }

        private Task<Leaderboard> GetLeaderboardOverallFromServiceAsync(string segmentId)
        {
            return GetLeaderboardFromServiceAsync(segmentId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="segmentId"></param>
        /// <param name="following"></param>
        /// <returns></returns>
        /// <remarks>
        /// When Strava flags a segment as Hazardous : we will receive an empty JSON payload!
        /// </remarks>
        private async Task<Leaderboard> GetLeaderboardFromServiceAsync(string segmentId, bool following = false)
        {
            try
            {
                var accessToken = await _settingsService.GetStoredStravaAccessTokenAsync();
                var defaultDistanceUnitType = await _settingsService.GetStoredDistanceUnitTypeAsync();

                //TODO: Glenn - Segment is needed to correctly calculate measurement values!
                Segment segment = await GetSegmentAsync(segmentId);

                string getUrl = $"{string.Format(Endpoints.Leaderboard, segmentId)}?access_token={accessToken}";
                if (following)
                    getUrl += "&following=true";
                string json = await _stravaWebClient.GetAsync(new Uri(getUrl));

                //TODO: Glenn - When we receive an empty JSON payload, this could mean the segment is marked as hazardous... do we notify user?

                var leaderboard = Unmarshaller<Leaderboard>.Unmarshal(json);
                if (leaderboard.Entries != null)
                {
                    foreach (LeaderboardEntry entry in leaderboard.Entries)
                    {
                        entry.Segment = segment;
                        StravaService.SetMetricUnits(entry, defaultDistanceUnitType);
                    }
                }

                return leaderboard;
            }
            catch (Exception ex)
            {
                string title = $"StravaSegmentService.GetLeaderboardFromServiceAsync - segmentId {segmentId} - following {following}";
                _logService.LogException(title, ex);
            }

            return null;
        }

        /// <summary>
        /// Gets all the starred segments of an Athlete.
        /// </summary>
        /// <returns>A list of segments that are starred by the athlete.</returns>
        private async Task<List<SegmentSummary>> GetStarredSegmentsFromServiceAsync(string athleteId)
        {
            try
            {
                var accessToken = await _settingsService.GetStoredStravaAccessTokenAsync();
                var defaultDistanceUnitType = await _settingsService.GetStoredDistanceUnitTypeAsync();

                string getUrl = $"{Endpoints.Athletes}/{athleteId}/segments/starred?access_token={accessToken}";
                string json = await _stravaWebClient.GetAsync(new Uri(getUrl));

                var segments = Unmarshaller<List<SegmentSummary>>.Unmarshal(json);
                foreach (SegmentSummary segment in segments)
                    StravaService.SetMetricUnits(segment, defaultDistanceUnitType);

                return segments;
            }
            catch (Exception ex)
            {
                string title = $"StravaSegmentService.GetStarredSegmentsFromServiceAsync - athleteId {athleteId}";
                _logService.LogException(title, ex);
            }

            return null;
        }

        public void FillStatistics(SegmentEffort segmentEffort, Leaderboard leaderboard)
        {
            if (leaderboard != null)
            {
                var entry = (from element in leaderboard.Entries
                             where element.AthleteId == segmentEffort.Athlete.Id
                             select element).FirstOrDefault();

                if (entry != null)
                {
                    //TODO: Glenn - Verify SegmentViewModel - There we also retrieve the corresponding Segment for MAP info, maybe better we do it here in the Service?? ( Merge/Combine )
                    //TODO: Glenn - moved segment setting logic into GetLeaderboardFromServiceAsync
                    //TODO: Glenn - each leaderboard entry should need a segment to calculate the averagespeed
                    //entry.Segment = segmentEffort.Segment;

                    StatisticsGroup pr = new StatisticsGroup() {Name = "personal record", Sort = 1, Type = StatisticGroupType.PR};
                    StatisticsDetail movingTimePR = new StatisticsDetail()
                    {
                        Sort = 0,
                        Icon = "",
                        DisplayDescription = "moving time",
                        DisplayValue =
                            $"{Helpers.Converters.SecToTimeConverter.Convert(entry.MovingTime, typeof (int), null, string.Empty)}",
                        Group = pr
                    };

                    StatisticsDetail averageSpeedPR = new UserMeasurementUnitStatisticsDetail(entry.AverageSpeedUserMeasurementUnit)
                    {
                        Sort = 1,
                        Icon = "",
                        DisplayDescription = "average speed",
                        Group = pr
                    };

                    StatisticsDetail averageHeartRatePR = new StatisticsDetail()
                    {
                        Sort = 2,
                        Icon = "",
                        DisplayDescription = "average heart rate",
                        DisplayValue = $"{Math.Round(entry.AverageHeartrateDisplay)} bpm",
                        Group = pr
                    };

                    StatisticsDetail rankPR = new StatisticsDetail()
                    {
                        Sort = 2,
                        Icon = "",
                        DisplayDescription = "rank",
                        DisplayValue = $"{entry.Rank}/{leaderboard.EntryCount}",
                        Group = pr
                    };

                    pr.Details.Add(movingTimePR);
                    pr.Details.Add(averageSpeedPR);
                    pr.Details.Add(averageHeartRatePR);
                    pr.Details.Add(rankPR);

                    segmentEffort.Statistics.Add(pr);
                }
            }
        }

        public Task<Segment> GetSegmentAsync(string segmentId)
        {
            return _cachedSegmentTasks.GetOrAdd(segmentId, GetSegmentFromServiceAsync);
        }

        public Task<SegmentEffort> GetSegmentEffortAsync(string segmentEffortId)
        {
            return _cachedSegmentEffortTasks.GetOrAdd(segmentEffortId, GetSegmentEffortFromServiceAsync);
        }

        public async Task<List<SegmentSummary>> GetStarredSegmentsAsync()
        {
            //TODO: Glenn - Caching?
            try
            {
                var accessToken = await _settingsService.GetStoredStravaAccessTokenAsync();
                var defaultDistanceUnitType = await _settingsService.GetStoredDistanceUnitTypeAsync();

                string getUrl = $"{Endpoints.Starred}?access_token={accessToken}";
                string json = await _stravaWebClient.GetAsync(new Uri(getUrl));

                var segments = Unmarshaller<List<SegmentSummary>>.Unmarshal(json);
                foreach (SegmentSummary segment in segments)
                    StravaService.SetMetricUnits(segment, defaultDistanceUnitType);

                return segments;
            }
            catch (Exception ex)
            {
                string title = "StravaSegmentService.GetStarredSegmentsAsync";
                _logService.LogException(title, ex);
            }

            return null;
        }

        public Task<List<SegmentSummary>> GetStarredSegmentsAsync(string athleteId)
        {
            return _cachedStarredSegmentsTasks.GetOrAdd(athleteId, GetStarredSegmentsFromServiceAsync);
        }

        public Task<Leaderboard> GetLeaderboardAsync(string segmentId, bool following = false)
        {
            if (following)
                return _cachedLeaderboardFollowingTasks.GetOrAdd(segmentId, GetLeaderboardFollowingFromServiceAsync);
            else
                return _cachedLeaderboardTasks.GetOrAdd(segmentId, GetLeaderboardOverallFromServiceAsync);
        }
    }
}
