import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Download, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { api } from '@/api/client';
import type { UserDto } from '@/api/types';
import { Badge, Modal, PageHeader } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { prettyDate } from '@/lib/format';
import { toast } from '@/lib/toast';

export function ProfilePage() {
  const user = useAuthStore((s) => s.user)!;
  const setUser = useAuthStore((s) => s.setUser);
  const clear = useAuthStore((s) => s.clear);
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const profile = useForm({ defaultValues: { displayName: user.displayName, bio: user.bio ?? '', preferredCurrency: user.preferredCurrency } });
  const pwd = useForm({ defaultValues: { currentPassword: '', newPassword: '' } });

  const save = useMutation({ mutationFn: (v: object) => api<UserDto>('/me/profile', { method: 'PUT', body: v }), onSuccess: (u) => { setUser(u); toast.success('Profile updated'); }, onError: (e) => toast.error((e as Error).message) });
  const changePassword = useMutation({ mutationFn: (v: object) => api('/auth/change-password', { method: 'POST', body: v }), onSuccess: () => { toast.success('Password changed. Other sessions were signed out.'); pwd.reset(); }, onError: (e) => toast.error((e as Error).message) });
  const avatar = useMutation({ mutationFn: (file: File) => { const f = new FormData(); f.append('file', file); return api<UserDto>('/me/avatar', { method: 'POST', body: f }); }, onSuccess: (u) => { setUser(u); toast.success('Photo updated'); }, onError: (e) => toast.error((e as Error).message) });
  const exportData = async () => {
    const data = await api<object>('/me/export');
    const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
    const a = Object.assign(document.createElement('a'), { href: url, download: 'staysphere-my-data.json' });
    a.click();
    URL.revokeObjectURL(url);
  };
  const deleteAccount = useMutation({ mutationFn: () => api('/me', { method: 'DELETE' }), onSuccess: () => { clear(); qc.clear(); toast.success('Your account was deleted.'); navigate('/'); }, onError: (e) => toast.error((e as Error).message) });

  return (
    <div className="container-page max-w-3xl py-10">
      <PageHeader title="Account" subtitle={`${user.email} · member since ${prettyDate(user.createdAt)}`} actions={<div className="flex gap-2">{user.roles.map((r) => <Badge key={r} tone="brand">{r}</Badge>)}{!user.emailConfirmed && <Badge tone="amber">Email not verified</Badge>}</div>} />
      <section className="card mb-6 p-6">
        <h2 className="mb-4 text-lg font-semibold">Profile</h2>
        <div className="mb-5 flex items-center gap-4">
          {user.avatarUrl ? <img src={user.avatarUrl} alt="" className="h-16 w-16 rounded-full object-cover" /> : <span className="flex h-16 w-16 items-center justify-center rounded-full bg-slate-800 text-2xl font-semibold text-white">{user.displayName[0]}</span>}
          <label className="btn-secondary cursor-pointer">Upload photo<input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" onChange={(e) => e.target.files?.[0] && avatar.mutate(e.target.files[0])} /></label>
        </div>
        <form className="space-y-4" onSubmit={profile.handleSubmit((v) => save.mutate(v))}>
          <div><label className="label" htmlFor="dn">Display name</label><input id="dn" className="input" {...profile.register('displayName', { required: true })} /></div>
          <div><label className="label" htmlFor="bio">About you</label><textarea id="bio" className="input" rows={3} {...profile.register('bio')} /></div>
          <div><label className="label" htmlFor="cur">Preferred currency</label><select id="cur" className="input" {...profile.register('preferredCurrency')}>{['USD', 'EUR', 'GBP', 'INR', 'JPY', 'AUD', 'CAD'].map((c) => <option key={c}>{c}</option>)}</select></div>
          <button className="btn-primary" disabled={save.isPending}>Save</button>
        </form>
      </section>
      <section className="card mb-6 p-6">
        <h2 className="mb-4 text-lg font-semibold">Password</h2>
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={pwd.handleSubmit((v) => changePassword.mutate(v))}>
          <div><label className="label" htmlFor="cp">Current password</label><input id="cp" type="password" className="input" autoComplete="current-password" {...pwd.register('currentPassword', { required: true })} /></div>
          <div><label className="label" htmlFor="np">New password</label><input id="np" type="password" className="input" autoComplete="new-password" {...pwd.register('newPassword', { required: true })} /></div>
          <div><button className="btn-secondary" disabled={changePassword.isPending}>Change password</button></div>
        </form>
      </section>
      <section className="card p-6">
        <h2 className="mb-2 text-lg font-semibold">Privacy</h2>
        <p className="mb-4 text-sm text-slate-600">Download a copy of your personal data, or permanently delete your account. Reviews and messages are anonymised.</p>
        <div className="flex flex-wrap gap-3">
          <button className="btn-secondary" onClick={exportData}><Download className="h-4 w-4" /> Export my data</button>
          <button className="btn-ghost text-red-700" onClick={() => setDeleteOpen(true)}><Trash2 className="h-4 w-4" /> Delete account</button>
        </div>
      </section>
      <Modal open={deleteOpen} onClose={() => setDeleteOpen(false)} title="Delete your account?">
        <p className="text-slate-700">This permanently removes your personal information. This cannot be undone.</p>
        <div className="mt-6 flex justify-end gap-3"><button className="btn-secondary" onClick={() => setDeleteOpen(false)}>Cancel</button><button className="btn-danger" onClick={() => deleteAccount.mutate()} disabled={deleteAccount.isPending}>Delete permanently</button></div>
      </Modal>
    </div>
  );
}
