// Types generated from the API's OpenAPI document (npm run gen:api). Never hand-edit schema.d.ts.
import type { components } from './schema';

type S = components['schemas'];

export type UserDto = S['Auth.UserDto'];
export type AuthResponse = S['Auth.AuthResponse'];
export type PropertyCard = S['Properties.PropertyCardDto'];
export type PropertyDetail = S['Properties.PropertyDetailDto'];
export type AmenityDto = S['Properties.AmenityDto'];
export type ImageDto = S['Properties.ImageDto'];
export type UpdatePropertyRequest = S['Properties.UpdatePropertyRequest'];
export type HostPropertyListItem = S['Properties.HostPropertyListItemDto'];
export type DestinationDto = S['Search.DestinationDto'];
export type GeocodeResult = S['Search.GeocodeResultDto'];
export type SearchSort = S['Search.SearchSort'];
export type QuoteDto = S['Booking.QuoteDto'];
export type AvailabilityDto = S['Booking.AvailabilityDto'];
export type ReservationDto = S['Booking.ReservationDto'];
export type CancellationPreview = S['Booking.CancellationPreviewDto'];
export type SeasonalPriceDto = S['Booking.SeasonalPriceDto'];
export type PaymentDto = S['Payments.PaymentDto'];
export type EarningsSummary = S['Payments.EarningsSummaryDto'];
export type ReviewDto = S['Reviews.ReviewDto'];
export type RatingSummary = S['Reviews.RatingSummaryDto'];
export type ConversationDto = S['Engagement.ConversationDto'];
export type MessageDto = S['Engagement.MessageDto'];
export type NotificationDto = S['Engagement.NotificationDto'];
export type TicketDto = S['Support.TicketDto'];
export type AdminDashboard = S['Admin.AdminDashboardDto'];
export type HostDashboard = S['Admin.HostDashboardDto'];
export type AdminUser = S['Admin.AdminUserDto'];
export type AdminProperty = S['Admin.AdminPropertyDto'];
export type AuditLog = S['Admin.AuditLogDto'];
export type FraudAlert = S['Admin.FraudAlertDto'];
export type ReportDto = S['Admin.ReportDto'];
export type AssistantReply = S['Ai.AssistantReplyDto'];
export type ConfirmActionResult = S['Ai.ConfirmActionResultDto'];
export type ExchangeRates = S['ExchangeRates'];

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages?: number;
  nextCursor?: string | null;
}

export interface WeatherDay { date: string; minC: number; maxC: number; weatherCode: number; summary: string }
export interface WeatherResult { currentTempC?: number | null; currentSummary?: string | null; daily: WeatherDay[]; source: string }
