import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { CheckCircle2, MailCheck } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useForm, type FieldValues, type Path, type UseFormSetError } from 'react-hook-form';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { api, ApiError } from '@/api/client';
import type { AuthResponse } from '@/api/types';
import { FieldError, Spinner } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { toast } from '@/lib/toast';

const password = z.string().min(10, 'At least 10 characters').regex(/[A-Z]/, 'Add an uppercase letter').regex(/[a-z]/, 'Add a lowercase letter').regex(/[0-9]/, 'Add a digit');

/** Maps server validation errors (ProblemDetails.errors) onto form fields. */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>): string | null {
  if (!(error instanceof ApiError)) return 'Something went wrong. Please try again.';
  if (error.errors) {
    for (const [field, messages] of Object.entries(error.errors)) setError(field as Path<T>, { message: messages[0] });
    return null;
  }
  return error.message;
}

function AuthShell({ title, subtitle, children, footer }: { title: string; subtitle?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="flex min-h-[70vh] items-center justify-center px-4 py-12">
      <div className="w-full max-w-md">
        <div className="card p-8">
          <h1 className="text-2xl font-bold">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-slate-600">{subtitle}</p>}
          <div className="mt-6">{children}</div>
        </div>
        {footer && <p className="mt-6 text-center text-sm text-slate-600">{footer}</p>}
      </div>
    </div>
  );
}

const loginSchema = z.object({ email: z.string().email('Enter a valid email'), password: z.string().min(1, 'Enter your password') });
type LoginForm = z.infer<typeof loginSchema>;

export function LoginPage() {
  const setSession = useAuthStore((s) => s.setSession);
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [formError, setFormError] = useState<string | null>(null);
  const { register, handleSubmit, setError, setValue, formState: { errors, isSubmitting } } = useForm<LoginForm>({ resolver: zodResolver(loginSchema) });

  const onSubmit = async (values: LoginForm) => {
    setFormError(null);
    try {
      const auth = await api<AuthResponse>('/auth/login', { method: 'POST', body: values, retryOnUnauthorized: false });
      setSession(auth);
      toast.success(`Welcome back, ${auth.user.displayName}!`);
      const returnTo = params.get('returnTo');
      navigate(returnTo?.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/');
    } catch (e) {
      setFormError(applyServerErrors(e, setError));
    }
  };

  const demo = (email: string) => {
    setValue('email', email);
    setValue('password', 'Passw0rd!Demo');
  };

  return (
    <AuthShell title="Welcome back" subtitle="Log in to manage your trips and listings." footer={<>New to StaySphere? <Link to="/register" className="font-semibold text-brand-700">Create an account</Link></>}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        {formError && <div role="alert" className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800">{formError}</div>}
        <div>
          <label className="label" htmlFor="email">Email</label>
          <input id="email" type="email" autoComplete="email" className="input" {...register('email')} aria-invalid={!!errors.email} />
          <FieldError message={errors.email?.message} />
        </div>
        <div>
          <div className="flex justify-between"><label className="label" htmlFor="password">Password</label><Link to="/forgot-password" className="text-sm text-brand-700">Forgot?</Link></div>
          <input id="password" type="password" autoComplete="current-password" className="input" {...register('password')} aria-invalid={!!errors.password} />
          <FieldError message={errors.password?.message} />
        </div>
        <button className="btn-primary w-full" disabled={isSubmitting}>{isSubmitting && <Spinner className="h-4 w-4" />} Log in</button>
      </form>
      <div className="mt-6 rounded-xl bg-slate-50 p-4 text-xs text-slate-600">
        <p className="mb-2 font-semibold text-slate-700">Demo accounts (password <code>Passw0rd!Demo</code>)</p>
        <div className="flex flex-wrap gap-2">
          {['guest', 'host', 'admin', 'support'].map((r) => (
            <button key={r} type="button" className="chip !py-1 !text-xs" onClick={() => demo(`${r}@example.local`)}>{r}@example.local</button>
          ))}
        </div>
      </div>
    </AuthShell>
  );
}

const registerSchema = z.object({ displayName: z.string().min(1, 'Tell us your name').max(80), email: z.string().email('Enter a valid email'), password });
type RegisterForm = z.infer<typeof registerSchema>;

export function RegisterPage() {
  const setSession = useAuthStore((s) => s.setSession);
  const navigate = useNavigate();
  const [formError, setFormError] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<RegisterForm>({ resolver: zodResolver(registerSchema) });

  const onSubmit = async (values: RegisterForm) => {
    setFormError(null);
    try {
      const auth = await api<AuthResponse>('/auth/register', { method: 'POST', body: values, retryOnUnauthorized: false });
      setSession(auth);
      toast.success('Account created — check your inbox to verify your email.');
      navigate('/');
    } catch (e) {
      setFormError(applyServerErrors(e, setError));
    }
  };

  return (
    <AuthShell title="Create your account" subtitle="Book unique stays and host your own space." footer={<>Already have an account? <Link to="/login" className="font-semibold text-brand-700">Log in</Link></>}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        {formError && <div role="alert" className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800">{formError}</div>}
        <div>
          <label className="label" htmlFor="displayName">Name</label>
          <input id="displayName" autoComplete="name" className="input" {...register('displayName')} />
          <FieldError message={errors.displayName?.message} />
        </div>
        <div>
          <label className="label" htmlFor="email">Email</label>
          <input id="email" type="email" autoComplete="email" className="input" {...register('email')} />
          <FieldError message={errors.email?.message} />
        </div>
        <div>
          <label className="label" htmlFor="password">Password</label>
          <input id="password" type="password" autoComplete="new-password" className="input" {...register('password')} />
          <FieldError message={errors.password?.message} />
          <p className="mt-1 text-xs text-slate-500">10+ characters with upper- and lowercase letters and a number.</p>
        </div>
        <button className="btn-primary w-full" disabled={isSubmitting}>{isSubmitting && <Spinner className="h-4 w-4" />} Sign up</button>
      </form>
    </AuthShell>
  );
}

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false);
  const { register, handleSubmit, formState: { isSubmitting } } = useForm<{ email: string }>();
  const onSubmit = async ({ email }: { email: string }) => {
    await api('/auth/forgot-password', { method: 'POST', body: { email } }).catch(() => undefined);
    setSent(true);
  };
  return (
    <AuthShell title="Reset your password" subtitle="We'll email you a secure link.">
      {sent ? (
        <div className="flex flex-col items-center text-center"><MailCheck className="h-10 w-10 text-brand-700" /><p className="mt-3">If an account exists for that email, a reset link is on its way.</p><p className="mt-1 text-xs text-slate-500">Local dev: open Mailpit at http://localhost:8025</p></div>
      ) : (
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <div><label className="label" htmlFor="email">Email</label><input id="email" type="email" required className="input" {...register('email')} /></div>
          <button className="btn-primary w-full" disabled={isSubmitting}>Send reset link</button>
        </form>
      )}
    </AuthShell>
  );
}

const resetSchema = z.object({ newPassword: password, confirm: z.string() }).refine((v) => v.newPassword === v.confirm, { message: 'Passwords do not match', path: ['confirm'] });
type ResetForm = z.infer<typeof resetSchema>;

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [formError, setFormError] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<ResetForm>({ resolver: zodResolver(resetSchema) });
  const onSubmit = async (v: ResetForm) => {
    try {
      await api('/auth/reset-password', { method: 'POST', body: { userId: params.get('userId'), token: params.get('token'), newPassword: v.newPassword } });
      toast.success('Password updated. Please log in.');
      navigate('/login');
    } catch (e) {
      setFormError(applyServerErrors(e, setError));
    }
  };
  return (
    <AuthShell title="Choose a new password">
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        {formError && <div role="alert" className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800">{formError}</div>}
        <div><label className="label" htmlFor="np">New password</label><input id="np" type="password" className="input" {...register('newPassword')} /><FieldError message={errors.newPassword?.message} /></div>
        <div><label className="label" htmlFor="cp">Confirm password</label><input id="cp" type="password" className="input" {...register('confirm')} /><FieldError message={errors.confirm?.message} /></div>
        <button className="btn-primary w-full" disabled={isSubmitting}>Update password</button>
      </form>
    </AuthShell>
  );
}

export function VerifyEmailPage() {
  const [params] = useSearchParams();
  const verify = useMutation({ mutationFn: () => api('/auth/verify-email', { method: 'POST', body: { userId: params.get('userId'), token: params.get('token') } }) });
  const { mutate } = verify;
  useEffect(() => { mutate(); }, [mutate]);
  return (
    <AuthShell title="Email verification">
      {verify.isPending && <div className="flex justify-center"><Spinner className="h-8 w-8" /></div>}
      {verify.isSuccess && <div className="text-center"><CheckCircle2 className="mx-auto h-10 w-10 text-emerald-600" /><p className="mt-3">Your email is verified. You're all set!</p><Link to="/" className="btn-primary mt-6">Start exploring</Link></div>}
      {verify.isError && <p role="alert" className="text-red-700">{(verify.error as Error).message}</p>}
    </AuthShell>
  );
}
