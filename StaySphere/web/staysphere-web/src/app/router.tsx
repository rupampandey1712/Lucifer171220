import { lazy, type ComponentType } from 'react';
import { createBrowserRouter } from 'react-router';
import { AdminLayout, HostLayout, RequireAuth, RootLayout } from './Layout';
import { HomePage } from '@/features/home/HomePage';
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyEmailPage } from '@/features/auth/AuthPages';
import { NotFoundPage } from '@/features/errors/NotFoundPage';

// Route-level code splitting keeps the first load small.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type AnyComponent = ComponentType<any>;
function lazyNamed<M, K extends keyof M>(loader: () => Promise<M>, name: K) {
  return lazy(async () => ({ default: (await loader())[name] as Extract<M[K], AnyComponent> }));
}

const SearchPage = lazyNamed(() => import('@/features/search/SearchPage'), 'SearchPage');
const PropertyPage = lazyNamed(() => import('@/features/property/PropertyPage'), 'PropertyPage');
const CheckoutPage = lazyNamed(() => import('@/features/booking/BookingPages'), 'CheckoutPage');
const TripsPage = lazyNamed(() => import('@/features/booking/BookingPages'), 'TripsPage');
const TripDetailPage = lazyNamed(() => import('@/features/booking/BookingPages'), 'TripDetailPage');
const FavoritesPage = lazyNamed(() => import('@/features/favorites/FavoritesPage'), 'FavoritesPage');
const MessagesPage = lazyNamed(() => import('@/features/messages/MessagesPage'), 'MessagesPage');
const NotificationsPage = lazyNamed(() => import('@/features/notifications/NotificationsPage'), 'NotificationsPage');
const ProfilePage = lazyNamed(() => import('@/features/profile/ProfilePage'), 'ProfilePage');
const SupportPage = lazyNamed(() => import('@/features/support/SupportPage'), 'SupportPage');
const BecomeHostPage = lazyNamed(() => import('@/features/host/HostPages'), 'BecomeHostPage');
const HostDashboardPage = lazyNamed(() => import('@/features/host/HostPages'), 'HostDashboardPage');
const HostPropertiesPage = lazyNamed(() => import('@/features/host/HostPages'), 'HostPropertiesPage');
const HostReservationsPage = lazyNamed(() => import('@/features/host/HostPages'), 'HostReservationsPage');
const HostEarningsPage = lazyNamed(() => import('@/features/host/HostPages'), 'HostEarningsPage');
const HostReviewsPage = lazyNamed(() => import('@/features/host/HostPages'), 'HostReviewsPage');
const HostCalendarPage = lazyNamed(() => import('@/features/host/HostCalendar'), 'HostCalendarPage');
const NewListingPage = lazyNamed(() => import('@/features/host/ListingWizard'), 'NewListingPage');
const EditListingPage = lazyNamed(() => import('@/features/host/ListingWizard'), 'EditListingPage');
const AdminDashboardPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminDashboardPage');
const AdminUsersPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminUsersPage');
const AdminPropertiesPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminPropertiesPage');
const AdminReservationsPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminReservationsPage');
const AdminPaymentsPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminPaymentsPage');
const AdminReviewsPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminReviewsPage');
const AdminReportsPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminReportsPage');
const AdminAuditPage = lazyNamed(() => import('@/features/admin/AdminPages'), 'AdminAuditPage');

const authed = (el: React.ReactNode) => <RequireAuth>{el}</RequireAuth>;

export const router = createBrowserRouter([
  {
    element: <RootLayout />,
    children: [
      { path: '/', element: <HomePage /> },
      { path: '/search', element: <SearchPage /> },
      { path: '/property/:id', element: <PropertyPage /> },
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
      { path: '/reset-password', element: <ResetPasswordPage /> },
      { path: '/verify-email', element: <VerifyEmailPage /> },
      { path: '/become-a-host', element: <BecomeHostPage /> },
      { path: '/book/:id', element: authed(<CheckoutPage />) },
      { path: '/trips', element: authed(<TripsPage />) },
      { path: '/trips/:id', element: authed(<TripDetailPage />) },
      { path: '/favorites', element: authed(<FavoritesPage />) },
      { path: '/messages', element: authed(<MessagesPage />) },
      { path: '/messages/:conversationId', element: authed(<MessagesPage />) },
      { path: '/notifications', element: authed(<NotificationsPage />) },
      { path: '/profile', element: authed(<ProfilePage />) },
      { path: '/support', element: authed(<SupportPage />) },
      {
        path: '/host',
        element: <HostLayout />,
        children: [
          { index: true, element: <HostDashboardPage /> },
          { path: 'properties', element: <HostPropertiesPage /> },
          { path: 'properties/new', element: <NewListingPage /> },
          { path: 'properties/:id/edit', element: <EditListingPage /> },
          { path: 'calendar', element: <HostCalendarPage /> },
          { path: 'reservations', element: <HostReservationsPage /> },
          { path: 'earnings', element: <HostEarningsPage /> },
          { path: 'reviews', element: <HostReviewsPage /> },
          { path: 'analytics', element: <HostDashboardPage analytics /> },
        ],
      },
      {
        path: '/admin',
        element: <AdminLayout />,
        children: [
          { index: true, element: <AdminDashboardPage /> },
          { path: 'users', element: <AdminUsersPage /> },
          { path: 'properties', element: <AdminPropertiesPage /> },
          { path: 'reservations', element: <AdminReservationsPage /> },
          { path: 'payments', element: <AdminPaymentsPage /> },
          { path: 'reviews', element: <AdminReviewsPage /> },
          { path: 'reports', element: <AdminReportsPage /> },
          { path: 'audit', element: <AdminAuditPage /> },
        ],
      },
      { path: '/404', element: <NotFoundPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
