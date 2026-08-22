import axios from 'axios';
import type {
  ChatHistory,
  ChatResponse,
  Manual,
  ManualManifest,
  UserManual,
  Vehicle
} from './types';

export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

export const http = axios.create({
  baseURL: apiBaseUrl,
  timeout: 30000
});

export function assetUrl(path: string): string {
  if (!path) {
    return '';
  }

  if (path.startsWith('http')) {
    return path;
  }

  if (apiBaseUrl.startsWith('/')) {
    return path;
  }

  return apiBaseUrl.replace(/\/api\/?$/, '') + path;
}

export async function login(username: string) {
  const { data } = await http.post('/auth/login', {
    username,
    password: 'demo'
  });
  return data as { userId: number; username: string; token: string };
}

export async function adminLogin(username: string, password: string) {
  const { data } = await http.post('/admin/auth/login', {
    username,
    password
  });
  return data as { username: string; token: string };
}

export async function getVehicles() {
  const { data } = await http.get('/vehicles');
  return data as Vehicle[];
}

export async function getVehicleManual(vehicleId: number) {
  const { data } = await http.get(`/vehicles/${vehicleId}/manual`);
  return data as UserManual;
}

export async function getManualManifest(manualId: number) {
  const { data } = await axios.get(assetUrl(`/manuals/${manualId}/manifest.json`));
  return data as ManualManifest;
}

export async function askManual(
  userId: number,
  vehicleId: number,
  question: string,
  conversationId: string
) {
  const { data } = await http.post('/chat', {
    userId,
    vehicleId,
    question,
    conversationId
  });
  return data as ChatResponse;
}

export async function getHistory(userId: number, vehicleId: number) {
  const { data } = await http.get('/chat/history', {
    params: {
      userId,
      vehicleId
    }
  });
  return data as ChatHistory[];
}

function adminHeaders(token: string) {
  return {
    Authorization: `Bearer ${token}`
  };
}

export async function getAdminManuals(token: string) {
  const { data } = await http.get('/admin/manuals', {
    headers: adminHeaders(token)
  });
  return data as Manual[];
}

export async function uploadManual(formData: FormData, token: string) {
  const { data } = await http.post('/admin/manuals', formData, {
    headers: adminHeaders(token)
  });
  return data as Manual;
}

export async function reprocessAdminManual(manualId: number, token: string) {
  const { data } = await http.post(`/admin/manuals/${manualId}/reprocess`, null, {
    headers: adminHeaders(token)
  });
  return data as Manual;
}

export async function deleteAdminManual(manualId: number, token: string) {
  await http.delete(`/admin/manuals/${manualId}`, {
    headers: adminHeaders(token)
  });
}
