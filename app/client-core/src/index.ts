export { createCichlidsClient, ApiError } from "./client.js";
export { ADOPTION_MAX_PHOTOS, adoptionProblemOf, toggleSuggestion } from "./adoption.js";
export type { AdoptionProblem, PhotoSuggestion, TankAdoption } from "./adoption.js";
export { handleProblemOf } from "./handle.js";
export { RETROSPECTIVE_CARD_MS, retrospectivePlayerMachine, statEntries } from "./retrospective.js";
export type { Retrospective, RetrospectiveCard, RetrospectiveImage } from "./retrospective.js";
export type { HandleProblem, HandleProblemReason } from "./handle.js";
export { uploadVideo } from "./videoUpload.js";
export type {
  LibraryDetail, LibraryItem, LibraryKind, LibraryListParams, LibraryPlace, LibraryPlaceKind, LibraryQuickShare, LibraryStreakWeek,
} from "./library.js";
export {
  STORY_CAPTION_MAX_LENGTH, STORY_DESCRIPTION_MAX_LENGTH, STORY_TITLE_MAX_LENGTH, STORY_TOPICS,
  canPublishStory, isValidCaption, moveEntry, normalizeCaption, storySequence,
} from "./storyEditor.js";
export type {
  EditorEntry, EditorItem, EditorSubject, MyStory, PublishableStoryTopic, StoryEditor, StorySubjectRequest, StoryTextRequest,
} from "./storyEditor.js";
export { HEATMAP_WEEKS, heatmapIntensity, heatmapListItems, layoutHeatmap } from "./careHeatmap.js";
export type { CareHeatmap, CareHeatmapDay, HeatmapCell, HeatmapKind, HeatmapLayout } from "./careHeatmap.js";
export { checkVacationDates } from "./vacation.js";
export { activeDecommissioning, operatingTime } from "./decommission.js";
export type { DecommissionImage, DecommissionScope, Decommissioning, TankOperation } from "./decommission.js";
export { proofCameraMachine } from "./proofCamera.js";
export {
  activeVacation, daysUntilDeadline, hasStreakHistory, seriesPhotoKeepsStreak, toVacationShare,
} from "./streaks.js";
export type {
  MeStreaks, OwnTankStreaks, TankStreaks, TimeLapseStreak, Vacation, VacationShare, WaterChangeStreak, WaterChangeStreakState,
} from "./streaks.js";
export { opensOnStart, returnCards } from "./returnRetrospective.js";
export type {
  ReturnCardKind, ReturnNextSteps, ReturnOverdueItem, ReturnRegionStory, ReturnRegionTank, ReturnResponses, ReturnRetrospective, ReturnTankRef,
} from "./returnRetrospective.js";
export { daysSince, updateImageUri, updatePath } from "./todaySections.js";
export type {
  FollowedImage, FollowedStory, FollowedTank, FollowedUpdate, FollowedUpdateKind, Milestone, MilestoneKind, TodayFollowed,
} from "./todaySections.js";
export type { CreateVideoUploadRequest } from "./generated/types.gen.js";
export type { UploadVideoOptions, VideoUploadSession, VideoUploadSource, VideoUploadTransport } from "./videoUpload.js";
export type {
  CareEntry,
  CarePhoto,
  CareTodo,
  WaterChangeSession,
  WaterChangeFinished,
  WaterChangeStep,
  WaterChangeStepState,
  TodoListItem,
  TodoListParams,
  TodoState,
  CareAmountRequest,
  CareDue,
  CareDueState,
  CareKind,
  CareParameter,
  CarePlan,
  CarePlanChange,
  CarePlanRule,
  CareReason,
  CareSeries,
  CareSeriesRange,
  LogCareRequest,
  MeasurementOrigin,
  MeasurementUnit,
  MeasurementValue,
  MeasurementValueRequest,
  DiscoverArchive,
  DiscoverItem,
  DueCareItem,
  DueStreak,
  LocalTime,
  WebPushConfig,
  WebPushSubscriptionRecord,
  WebPushSubscriptionRequest,
  CichlidsClient,
  Video,
  VideoState,
  CichlidsClientOptions,
  DeviceTokenSource,
  StoriesListParams,
  TanksListParams,
  SpeciesListParams,
  CommunityThreadsListParams,
  PageParams,
  PagedResponse,
  StoryListItem,
  StoryDetail,
  StoryItem,
  StorySubject,
  TankListItem,
  TankDetail,
  TankTimeLapse,
  TankTimeLapsePhoto,
  TankMediaItem,
  Dimensions,
  WaterValues,
  Inhabitant,
  ProfileDetail,
  ProfileImageRole,
  SpeciesListItem,
  SpeciesDetail,
  CommunityCategory,
  CommunityThreadListItem,
  CommunityThreadDetail,
  CommunityPostDto,
  CommunityAttachment,
  SpeciesLink,
  Comment,
  CommentCreated,
  CreateCommentRequest,
  CreateTankRequest,
  TankVisibility,
  TankVisibilityDto,
  TankFollow,
  TankFollowState,
  TankAudience,
  TankMember,
  OnboardingRequest,
  OnboardingResultDto,
  LegacyTankDto,
  TankRegion,
  Me,
  MeSettings,
  HandleChange,
  Today,
  Draft,
  UploadFile,
  PublishStoryRequest,
} from "./client.js";
export { navigationMachine, selectedTab, canonicalPath } from "./navigation.js";
export type { NavigationTab, NavigationEvent, NavigationContext } from "./navigation.js";
export { tankStoryMachine } from "./tankStory.js";

export { careMachine } from "./care.js";
export { MAX_PHOTOS, NOTE_MAX_LENGTH, TODO_MAX_LENGTH, careAttachmentsMachine, isValidNote, normalizeNote, normalizeTodoText, todoListMachine } from "./careAttachments.js";
export { MAX_RHYTHM_DAYS, MIN_RHYTHM_DAYS, RHYTHM_CHOICES, TODAY_DUE_HORIZON_DAYS, dueCareForToday, dueSummary, isValidRhythm, parseRhythm } from "./carePlan.js";
export type { DueSummary } from "./carePlan.js";
export {
  MAX_SCHEDULED_REMINDERS, REMINDER_HOUR, REMINDER_MINUTE, activeSnoozes, isReminderId, localDateOf, planReminders, reconcileReminders, reminderFromSnooze, reminderId, snoozeReminder,
} from "./careReminders.js";
export type { PlannedReminder, ReminderInput, ReminderSnooze } from "./careReminders.js";
export {
  COMMON_PARAMETERS, MEASUREMENT_ORIGINS, MEASUREMENT_PARAMETERS, createDraft, draftWhen, setWhen, editDraft, fromCanonical, inputUnit,
  measurementMachine, PPM_PER_GERMAN_DEGREE, scaleSeries, seriesForDisplay, setField, setOrigin, showMore, validateDraft, valueInSystem, visibleParameters,
} from "./measurement.js";
export { customFromInstant, deviceTimeZone, localToUtc, resolveWhen, whenFromEntry } from "./occurredAt.js";
export type { LocalDateTime, ResolvedWhen, WhenChoice, WhenError } from "./occurredAt.js";
export type { Clock, DisplayPoint, DisplaySeries, DraftErrors, MeasurementDraft, ScaledPoint, UnitSystem } from "./measurement.js";
export {
  WATER_CHANGE_MAX_PHOTOS, WATER_CHANGE_STAGES, drainingSeconds, formatElapsed, nextWaterChangeStage, totalSeconds, waterChangeMachine,
} from "./waterChange.js";
export type { StoredWaterChange, WaterChangeActor, WaterChangeEffects, WaterChangeStage, WaterChangeTip } from "./waterChange.js";
export { DEFAULT_OVERLAY_OPACITY, latestSeriesPhotoUrl, overlayOpacity, timeLapseCameraMachine } from "./timeLapseCamera.js";

export { PAIRING_WINDOW_MS, layoutChart, loadWaterChanges, pairWaterChanges } from "./waterChangeMarks.js";
export type { ChartLayout, ChartLayoutOptions, LoadedWaterChange, PlacedMarker, WaterChangeLoadOptions, WaterChangeMark, WaterChangePair } from "./waterChangeMarks.js";

export { DeviceAuthError, createDeviceAuth, deviceSignatureMessage, toBase64Url } from "./deviceAuth.js";
export type { DeviceAuth, DeviceAuthOptions, DeviceCredential, DeviceCredentialStore, DeviceKeyProvider, DeviceProof, DeviceToken } from "./deviceAuth.js";
export { REGISTRATION_REQUIRED_CODE } from "./registration.js";
export type { ProvisionalContent, RegistrationMode, RegistrationMove, RegistrationPreview } from "./registration.js";
export { createWebCryptoKeyProvider } from "./deviceAuthWebCrypto.js";
export {
  REGISTRATION_REASONS, completeRegistration, droppedVacations, hasProvisionalContent, isRegistrationRequired,
} from "./registrationFlow.js";
export type { RegistrationOutcome, RegistrationReason } from "./registrationFlow.js";
