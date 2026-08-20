import { createApp } from 'vue';
import {
  ElButton,
  ElDialog,
  ElInput,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTooltip
} from 'element-plus';
import 'element-plus/dist/index.css';
import App from './App.vue';
import './styles.css';

const app = createApp(App);

app
  .use(ElButton)
  .use(ElDialog)
  .use(ElInput)
  .use(ElOption)
  .use(ElSelect)
  .use(ElTable)
  .use(ElTableColumn)
  .use(ElTooltip)
  .mount('#app');
