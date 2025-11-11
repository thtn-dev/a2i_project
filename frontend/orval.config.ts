import { defineConfig } from 'orval';

export default defineConfig({
  a2i: {
    input: {
      target: 'http://localhost:5087/openapi/v1.json',
    },
    output: {
      mode: 'tags-split',
      target: './lib/api/generated',
      schemas: './lib/api/generated/model',
      client: 'react-query',
      httpClient: 'axios',
      mock: false,
      prettier: true,
      override: {
        mutator: {
          path: './lib/api/custom-instance.ts',
          name: 'customInstance',
        },
        query: {
          useQuery: true,
          useInfinite: false,
          useInfiniteQueryParam: 'page',
        },
      },
    },
  },
});
