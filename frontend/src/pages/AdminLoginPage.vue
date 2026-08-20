<template>
  <main class="admin-login-shell">
    <section class="admin-login-panel">
      <div class="login-identity">
        <button class="login-back" type="button" @click="$emit('goUser')">
          <ArrowLeft :size="16" />
          返回用户端
        </button>

        <div class="login-brand">
          <div class="brand-mark">
            <CarFront :size="23" />
          </div>
          <div>
            <strong>手册助手</strong>
            <span>MANUAL AI</span>
          </div>
        </div>

        <div class="login-statement">
          <FileStack :size="28" />
          <h1>让每一页手册<br />都能被准确找到</h1>
          <p>维护车型资料、解析状态与引用原页。</p>
        </div>

        <div class="login-security">
          <ShieldCheck :size="16" />
          <span>管理操作受独立账号保护</span>
        </div>
      </div>

      <div class="login-form-side">
        <div class="login-heading">
          <span class="header-kicker">内容中心</span>
          <h2>管理员登录</h2>
          <p>登录后可上传和维护用户手册。</p>
        </div>

        <form class="admin-login-form" @submit.prevent="submit">
          <label>
            <span>管理员账号</span>
            <el-input
              v-model="username"
              size="large"
              placeholder="请输入账号"
              autocomplete="username"
            />
          </label>
          <label>
            <span>密码</span>
            <el-input
              v-model="password"
              size="large"
              placeholder="请输入密码"
              show-password
              type="password"
              autocomplete="current-password"
            />
          </label>
          <el-button
            class="login-submit"
            native-type="submit"
            size="large"
            :loading="loading"
          >
            登录内容中心
            <ArrowRight :size="17" />
          </el-button>
        </form>
      </div>
    </section>
  </main>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { ElMessage } from 'element-plus';
import {
  ArrowLeft,
  ArrowRight,
  CarFront,
  FileStack,
  ShieldCheck
} from 'lucide-vue-next';
import { adminLogin } from '../api';

const emit = defineEmits<{
  loggedIn: [token: string];
  goUser: [];
}>();

const username = ref('admin');
const password = ref('');
const loading = ref(false);

async function submit() {
  if (!username.value.trim() || !password.value) {
    ElMessage.warning('请输入账号和密码');
    return;
  }

  loading.value = true;

  try {
    const session = await adminLogin(username.value.trim(), password.value);
    emit('loggedIn', session.token);
  } catch {
    ElMessage.error('账号或密码不正确');
  } finally {
    loading.value = false;
  }
}
</script>
