<template>
  <AdminLoginPage
    v-if="isAdminRoute && !adminToken"
    @logged-in="handleAdminLogin"
    @go-user="goUser"
  />
  <AdminDashboard
    v-else-if="isAdminRoute"
    :admin-token="adminToken"
    @logout="handleAdminLogout"
    @go-user="goUser"
  />
  <UserChatPage v-else @go-admin="goAdmin" />
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue';
import UserChatPage from './pages/UserChatPage.vue';

const AdminDashboard = defineAsyncComponent(() => import('./pages/AdminDashboard.vue'));
const AdminLoginPage = defineAsyncComponent(() => import('./pages/AdminLoginPage.vue'));

const currentPath = ref(window.location.pathname);
const adminToken = ref(sessionStorage.getItem('adminToken') ?? '');

// 早期版本把后台令牌放在 localStorage。现在改为会话级保存，
// 浏览器关闭后自动失效，降低公共电脑上误保留后台登录状态的风险。
localStorage.removeItem('adminToken');

const isAdminRoute = computed(() => currentPath.value.startsWith('/admin'));

onMounted(() => {
  window.addEventListener('popstate', syncPath);
});

onUnmounted(() => {
  window.removeEventListener('popstate', syncPath);
});

function syncPath() {
  currentPath.value = window.location.pathname;
}

function navigate(path: string) {
  window.history.pushState({}, '', path);
  syncPath();
}

function handleAdminLogin(token: string) {
  adminToken.value = token;
  sessionStorage.setItem('adminToken', token);
  navigate('/admin');
}

function handleAdminLogout() {
  adminToken.value = '';
  sessionStorage.removeItem('adminToken');
  navigate('/admin');
}

function goAdmin() {
  navigate('/admin');
}

function goUser() {
  navigate('/');
}
</script>
