import React, { useState, useEffect } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { 
  ThemeProvider, 
  createTheme, 
  CssBaseline, 
  Container, 
  AppBar, 
  Toolbar, 
  Typography, 
  Box, 
  CircularProgress,
  Alert,
  Snackbar
} from '@mui/material';
import { OrderList } from './components/OrderList';
import { useOrders } from './hooks/useOrders';

const queryClient = new QueryClient();

const theme = createTheme({
  palette: {
    primary: {
      main: '#1976d2',
    },
    secondary: {
      main: '#dc004e',
    },
    background: {
      default: '#f5f5f5',
    },
  },
  typography: {
    h4: {
      fontWeight: 600,
    },
  },
});

function AppContent() {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(10);
  const [error, setError] = useState<string | null>(null);
  
  const { 
    data, 
    isLoading, 
    isError, 
    error: queryError 
  } = useOrders({
    page,
    pageSize: rowsPerPage,
  });

  useEffect(() => {
    if (isError && queryError) {
      setError(
        queryError instanceof Error 
          ? queryError.message 
          : 'Ocorreu um erro ao carregar os pedidos'
      );
    }
  }, [isError, queryError]);

  const handleCloseError = () => {
    setError(null);
  };

  const handleChangePage = (_: unknown, newPage: number) => {
    setPage(newPage);
  };

  const handleChangeRowsPerPage = (event: React.ChangeEvent<HTMLInputElement>) => {
    setRowsPerPage(parseInt(event.target.value, 10));
    setPage(0);
  };

  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            AMBEV Order Service
          </Typography>
        </Toolbar>
      </AppBar>
      <Container maxWidth="lg" sx={{ mt: 4, mb: 4 }}>
        <Typography variant="h4" gutterBottom>
        Order Service Frontend
        </Typography>
        
        <OrderList
          orders={data || []}
          loading={isLoading}
          totalCount={data?.length || 0}
          page={page}
          rowsPerPage={rowsPerPage}
          onPageChange={handleChangePage}
          onRowsPerPageChange={handleChangeRowsPerPage}
        />

        <Snackbar
          open={!!error}
          autoHideDuration={6000}
          onClose={handleCloseError}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        >
          <Alert onClose={handleCloseError} severity="error" sx={{ width: '100%' }}>
            {error}
          </Alert>
        </Snackbar>
      </Container>
    </Box>
  );
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <AppContent />
        <ReactQueryDevtools initialIsOpen={false} />
      </ThemeProvider>
    </QueryClientProvider>
  );
}

export default App;
